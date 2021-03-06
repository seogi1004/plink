﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Fiddler;
using PLinkCore;

[assembly: Fiddler.RequiredVersion("2.2.8.6")]

namespace PLink 
{

    public class PLinkAddOn : PLinkCore.PLink, IAutoTamper, IAutoTamper2, IAutoTamper3 
    {
    	public override void log(string str) { 
     		FiddlerApplication.Log.LogString(str);
    	}
    	
    	public PLinkAddOn(): base() { 
    		
    	}
    	
		public override void initializeUI() { }        

        public void AutoTamperRequestBefore(Session oSession) 
        {
			// http:443 프로토콜 체크
			if (PLinkCore.PLink.host.isHttpsFilter) { checkHttpsFilter(oSession); }
			
			// api 모드 적용
			if (oSession.HostnameIs("api.plink")) {
				runApiMode(oSession);
				return;
			}
			
			// PLink host 변조 적용 
			if (PLinkCore.PLink.host.StartState) {
				checkHost(oSession);
			}        	

        }
        
        // HOST, URL, REAL 타입체크 
		void checkHost(Session oSession)
		{
			HostCheck patternCheck = PLinkCore.PLink.host.checkPattern(oSession.fullUrl);
			
			if (patternCheck != null && patternCheck.Checked) {
				bool isCheck = checkUrlType(patternCheck, oSession);
				if (!isCheck) return;
				//return;
			}
			
			HostCheck check = PLinkCore.PLink.host.checkUrl(oSession.hostname, oSession.PathAndQuery);
			
			if (check == null) return;
			if (!check.Checked) return;
			
			// Host, Real 적용 
			if (check.isHost() || check.isReal()) {		
				if (oSession.HTTPMethodIs("CONNECT")) {
					oSession.hostname = check.afterHost();
				} else {
					oSession.bypassGateway = true;
					oSession["x-overrideHost"] = check.After;
				}
			} 
			// URL, PATTERN 적용 
			else if (check.isUrl() || check.isPattern()) {
				if (oSession.HTTPMethodIs("CONNECT")) {
					oSession.hostname = check.afterHost();
				} else {
					// 캐쉬된 정책 적용 
					string redirect = check.getHostItem().Redirect;
					if (!string.IsNullOrEmpty(redirect)) { 
						check.After = redirect;
					}
					oSession.fullUrl = check.afterUrl(oSession.fullUrl);
				}
			}
		}
		
		void sendResponse(Session oSession, int code, string content_type, byte[] data) {
			oSession.utilCreateResponseAndBypassServer();			
			oSession.oResponse.headers.HTTPResponseCode = code;
			
			string status = Util.getStatus(code);
			
			oSession.oResponse.headers.HTTPResponseStatus = status;
			
			oSession.oResponse.headers["Content-Type"] = content_type;
			//oSession.oResponse.headers["Content-Length"] = data.Length.ToString();
			
			log(data.ToString());
			
			oSession.ResponseBody = data;
		}			

		// 패턴 체크 실행 
		bool checkUrlType(HostCheck patternCheck, Session oSession)
		{
			if (oSession.HTTPMethodIs("CONNECT")) {
				oSession.hostname = patternCheck.afterHost();
			} else {
				
				if (patternCheck.isStatus()) { 
					int status = patternCheck.getStatusCode();
					
					sendResponse(oSession, status, "text/html", new byte[0]);
					
					return false; 
				}					
				
				if (patternCheck.isFolder() || patternCheck.isFile()) {
					string url = oSession.fullUrl;
					int idx = url.LastIndexOf(patternCheck.Before);
					
					FileInfo file;
					string target;
					
					if (patternCheck.isFolder()) { 
						
						string first = url.Substring(0, idx);
						string second = patternCheck.Before;
						string last = url.Replace(first, "").Replace(second, "");
						
						log(first + " : " + second + " : " + last);
						
						// 기본 디렉토리 지정 
						if (string.IsNullOrEmpty(last) || last.Equals("/")) {
							last = "/index.html";	
						}
						
						if (last[0] != '/') {  last = "/" + last; }
						
						target = patternCheck.After + last;
					} else { 
						target = patternCheck.After;
					}
					
					file = new FileInfo(target);
					
					if (file.Exists) { 
						string content_type = MimeType.Get(file.Extension);
						byte[] data = File.ReadAllBytes(file.FullName);
						
						sendResponse(oSession, 200, content_type, data);
					
						return false; 						
					} 
					
				} else { 				
					oSession.fullUrl = patternCheck.afterUrl(oSession.fullUrl);
				}
				
			}
					
			return true; 			
		}

		// api 모드 실행 
		void runApiMode(Session oSession)
		{
			PLinkApiType data = router(oSession.PathAndQuery);
			
			if (data == null) {
				oSession.oRequest.pipeClient.End();
			} else {
				SetDiabledCache(oSession);				
				// 새로운 응답 만들기 
				oSession.utilCreateResponseAndBypassServer();
				oSession.oResponse.headers.HTTPResponseCode = 200;
				oSession.oResponse.headers.HTTPResponseStatus = "200 OK";
				oSession.oResponse.headers["Content-Type"] = data.ContentType;
				SetDiabledCacheAfter(oSession);				
				oSession.utilSetResponseBody(data.Body);
			}
		}
		
		// Disabled Cache 적용 - Request
		void SetDiabledCache(Session oSession)
		{
			oSession.oRequest.headers.Remove("If-None-Match");
			oSession.oRequest.headers.Remove("If-Modified-Since");
			oSession.oRequest["Pragma"] = "no-cache";
		}
		
		// Disabled Cache 적용 - Response
		void SetDiabledCacheAfter(Session oS)
		{
			oS.oResponse.headers.Remove("Expires");
			oS.oResponse["Cache-Control"] = "no-cache";
		}	

		void checkHttpsFilter(Session oSession)
		{
			if (Regex.IsMatch(oSession.fullUrl, "^(http)://(.*):443/")) {
				oSession.fullUrl = Regex.Replace(oSession.fullUrl, "^(http)://(.*):443/", delegate(Match match) {
                 	string change = string.Format("{0}s://{1}/", match.Groups[1].ToString(), match.Groups[2].ToString());
                 	return change;
                 });
			}
		}

        public void AutoTamperRequestAfter(Session oSession) { }
        public void AutoTamperResponseBefore(Session oSession) { }
        public void AutoTamperResponseAfter(Session oSession) { }
        public void OnBeforeReturningError(Session oSession) { }
    	
        public void OnBeforeUnload() { 
        	this.OnUnload();
        }
        
		void IAutoTamper2.OnPeekAtResponseHeaders(Session oS)
		{
			// 메모리에 담아두지 않음 
            if (oS.oResponse.headers.Exists("Content-Length"))
            {
                int iLen = 0;
                if (int.TryParse(oS.oResponse["Content-Length"], out iLen))
                {
                    if (iLen > 5000000)
                    {
                        oS.bBufferResponse = false;
                        oS["log-drop-response-body"] = "save memory";
                    }
                }
            }

		}
    	
		void IAutoTamper3.OnPeekAtRequestHeaders(Session oSession)
		{
			// 메모리에 담아두지 않음 
			if (oSession.oRequest.headers.Exists("Content-Length")) { 
				int iLen = 0;
				if (int.TryParse(oSession.oRequest["Content-Length"], out iLen)) { 
					if (iLen > 1000000) { 
						oSession["log-drop-request-body"] = "save memory for request";
					}
				}
			}
		}
		
    	
		public override ContextMenuStrip getMenuStrip()
		{
			return FiddlerApplication.UI.mnuNotify;
		}
    	
		public override TabControl.TabPageCollection getTabPages()
		{
			return FiddlerApplication.UI.tabsViews.TabPages;
		}
    	
		public override void OnTabStateChange(TabPage tab)
		{
        	FiddlerApplication.UI.tabsViews.ImageList.Images.Add((System.Drawing.Image)hostTab.imageList2.Images[0]);
        	tab.ImageIndex = FiddlerApplication.UI.tabsViews.ImageList.Images.Count - 1;
		}
    	
		public override void SetConfig(string key, string value)
		{
			if (key.StartsWith("HeaderEncoding")) { 
				CONFIG.oHeaderEncoding = Encoding.GetEncoding(value);
			}
		}
    	
		public override void initCapture()
		{
			FiddlerApplication.oProxy.Attach();
		}
    	
		public override string getAppName()
		{
			return Util.APP_FIDDLER;
		}
    }
}
