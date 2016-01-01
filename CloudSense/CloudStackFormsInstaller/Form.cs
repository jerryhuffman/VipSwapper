using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.Serialization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Web;

namespace CloudStack
{
    public partial class Form : System.Windows.Forms.Form
    {
        public static string ClientID = "9d6614ce-9a62-464f-b0c6-3c97120fb98a";
        public static string RedirectUri = "http://www.vipswapper.com/cloudstack";
        public static string GraphResourceUri = "https://graph.windows.net";
        public static string GraphUrl = "https://graph.windows.net/{0}?api-version=1.6";
        public static string ARMResourceUri = "https://management.core.windows.net/";
        public static string AuthorizeUrl = "https://login.microsoftonline.com/{0}/oauth2/authorize?api-version=1.0";
        public static string TokenUrl = "https://login.microsoftonline.com/{0}/oauth2/token?api-version=1.0";
        public static string KeyCredentialPath = @"C:\CloudStack";
        public string AADId;

        public Form()
        {
            InitializeComponent();
            label1.Visible = true;
            DummyMethodDownloadCloudStackBinaries();
            labelCheck1.Visible = true;
            label2.Visible = true;
            DummyMethodInstallCloudStackWebsite();
            labelCheck2.Visible = true;
            label3.Visible = true;
            textBox.Visible = true;
            button.Visible = true;
            //GetAADForSubscription(subscriptionId);
            //DeviceAuthCodeResponse codeResponse = installer.GetDeviceAuthCode(aadId);
            //Console.Write("Got it. \nNow, you must authenticate with the account that you use to manage your Azure cloud - ");
            //Console.ForegroundColor = ConsoleColor.Cyan;
            //Console.Write("use a web browser to open the page {0} and enter the code {1}.", codeResponse.VerificationUrl, codeResponse.UserCode);
            //Console.ResetColor();
            //Console.Write("\nPatiently waiting ... ");
            //DeviceAuthTokenResponse tokenResponse = installer.GetDeviceAuthToken(codeResponse, aadId);
            //AADUser user = installer.GetAzureADUser(tokenResponse.AccessToken);
            //Console.Write("Ah! welcome {0}! Registering CloudStack in your Azure Active Directory now ... ", user.DisplayName);
            //installer.RegisterAzureADApplication(tokenResponse.AccessToken, "http://localhost");
            //Console.Write(" All done.");
        }

        public string GetAADForSubscription(string subscriptionId)
        {
            string aadId = null;

            string url = string.Format("https://management.azure.com/subscriptions/{0}?api-version=2014-04-01", subscriptionId);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            WebResponse response = null;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                if (ex.Response != null && ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.Unauthorized)
                {
                    string authUrl = ex.Response.Headers["WWW-Authenticate"].Split(',')[0].Split('=')[1];
                    aadId = authUrl.Substring(authUrl.LastIndexOf('/') + 1, 36);
                }
            }

            return aadId;
        }

        public AADUser GetAzureADUser(string accessToken)
        {
            AADUser user = null;
            string url = string.Format(GraphUrl, "me");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("{0} {1}", "Bearer", accessToken));
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            WebResponse response = request.GetResponse();
            using (Stream stream = response.GetResponseStream())
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AADUser));
                user = (AADUser)ser.ReadObject(stream);
            }
            return user;
        }

        public void RegisterAzureADApplication(string accessToken, string Url)
        {
            AADApplication app = new AADApplication();
            app.DisplayName = "CloudStack-" + Environment.MachineName;
            app.Homepage = Url;
            app.IdentifierUris = new string[1] { Url };
            app.ReplyUrls = new string[1] { Url };
            app.RequriredResourceAccess = new AADRequriredResourceAccess[2] {
                new AADRequriredResourceAccess {
                    //CloudStack needs delegated access to Azure Active Directory Graph API
                    ResourceAppId = "00000002-0000-0000-c000-000000000000",
                    ResourceAccess = new AADResourceAccess [2] {
                        //Sign-in and read user profile OAuth2Permission
                        new AADResourceAccess { Id = "311a71cc-e848-46a1-bdf8-97ff7156d8e6", Type = "Scope" },
                        //Read all users' basic profiles OAuth2Permission
                        new AADResourceAccess { Id = "cba73afc-7f69-4d86-8450-4978e04ecd1a", Type = "Scope" }
                    }
                },
                new AADRequriredResourceAccess {
                    //CloudStack needs delegated access to Azure Resource Manager API
                    ResourceAppId = "797f4846-ba00-4fd7-ba43-dac1f8f63013",
                    ResourceAccess = new AADResourceAccess [1] {
                        //Access Azure Service Management OAuth2Permission
                        new AADResourceAccess { Id = "41094075-9dad-400e-a0bd-54e686782033", Type = "Scope" }
                    }
                }
            };
            app.KeyCredentials = new AADKeyCredential[1] {
                CreateAzureADKeyCredential(KeyCredentialPath)
            };
            var existingApp = GetAzureADApplication(accessToken, app.DisplayName);
            if (existingApp != null) RemoveAzureADApplication(accessToken, existingApp.ObjectId);
            CreateAzureADApplication(accessToken, app);
        }

        private AADApplication GetAzureADApplication(string accessToken, string displayName)
        {
            AADApplication app = null;
            string url = string.Format(GraphUrl + "&$filter=displayName eq '{1}'"
                , "myorganization/applications", displayName);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("{0} {1}", "Bearer", accessToken));
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            WebResponse response = request.GetResponse();
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader sr = new StreamReader(stream))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AADApplicationResult));
                    var appResult = (AADApplicationResult)ser.ReadObject(stream);
                    if (appResult.Applications.Length > 0) app = appResult.Applications[0];
                }
            }
            return app;
        }

        private void RemoveAzureADApplication(string accessToken, string objectId)
        {
            string url = string.Format(GraphUrl, "myorganization/applications/" + objectId);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "DELETE";
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("{0} {1}", "Bearer", accessToken));
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            WebResponse response = request.GetResponse();
        }

        private void CreateAzureADApplication(string accessToken, AADApplication app)
        {
            string url = string.Format(GraphUrl, "myorganization/applications/");
            string postData;
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AADApplication));
            using (MemoryStream stream = new MemoryStream())
            {
                ser.WriteObject(stream, app);
                postData = Encoding.Default.GetString(stream.ToArray());
            }
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            byte[] data = encoding.GetBytes(postData);
            request.Method = "POST";
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("{0} {1}", "Bearer", accessToken));
            request.ContentType = "application/json";
            request.ContentLength = data.Length;
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }
            WebResponse response = request.GetResponse();
        }

        private AADKeyCredential CreateAzureADKeyCredential(string keyCredentialPath)
        {
            AADKeyCredential keyCred;

            var certificate = CertHelper.CreateCertificate(new X500DistinguishedName("CN=CloudStack AzureAD KeyCredential"), "CloudStack AzureAD KeyCredential");
            byte[] certData = certificate.Export(X509ContentType.Pfx);
            if (File.Exists(Path.Combine(keyCredentialPath, "CloudStackKeyCredential.pfx")))
                File.Delete(Path.Combine(keyCredentialPath, "CloudStackKeyCredential.pfx"));
            File.WriteAllBytes(Path.Combine(keyCredentialPath, "CloudStackKeyCredential.pfx"), certData);

            keyCred = new AADKeyCredential();
            keyCred.KeyId = Guid.NewGuid().ToString();
            keyCred.Type = "AsymmetricX509Cert";
            keyCred.Usage = "Verify";
            keyCred.CustomKeyIdentifier = Convert.ToBase64String(certificate.GetCertHash());
            keyCred.Value = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));

            return keyCred;
        }

        public void DummyMethodDownloadCloudStackBinaries()
        {

        }

        public void DummyMethodInstallCloudStackWebsite()
        {

        }

        private void button_Click(object sender, EventArgs e)
        {
            this.textBox.Enabled = false;
            this.button.Enabled = false;
            AADId = GetAADForSubscription(textBox.Text);
            string authorizeUrl = string.Format("{0}&response_type=code&client_id={1}&redirect_uri={2}&resource={3}&nonce={4}" +
                "&response_mode=form_post&prompt=login", string.Format(AuthorizeUrl, AADId), ClientID, System.Uri.EscapeDataString(RedirectUri),
                System.Uri.EscapeDataString(GraphResourceUri), Guid.NewGuid().ToString());
            this.webBrowser.Visible = true;
            this.webBrowser.Navigate(authorizeUrl);
        }

        private delegate void BeforeNavigate2(object pDisp, ref dynamic url, ref dynamic Flags, ref dynamic TargetFrameName, ref dynamic PostData, ref dynamic Headers, ref bool Cancel);

        private void Form_Load(object sender, EventArgs e)
        {
            this.webBrowser.GoBack();
            dynamic d = this.webBrowser.ActiveXInstance;

            d.BeforeNavigate2 += new BeforeNavigate2((object pDisp,
                ref dynamic url,
                ref dynamic Flags,
                ref dynamic TargetFrameName,
                ref dynamic PostData,
                ref dynamic Headers,
                ref bool Cancel) =>
            {
                if (url.Equals(RedirectUri.Trim()) && PostData != null)
                {
                    Cancel = true;
                    string responseString = Encoding.UTF8.GetString(PostData);
                    ProcessAuth(responseString);
                }
            });
        }

        private void ProcessAuth(string responseString)
        {
            this.webBrowser.Visible = false;
            var nvc = HttpUtility.ParseQueryString(responseString);
            string code = nvc["code"];
            DeviceAuthTokenResponse tokenResponse = GetAccessToken(code);
            AADUser user = GetAzureADUser(tokenResponse.AccessToken);
            label4.Text = string.Format("Ah! welcome {0}! Registering CloudStack in your Azure Active Directory now ... ", user.DisplayName);
            label4.Visible = true;
            RegisterAzureADApplication(tokenResponse.AccessToken, "http://localhost");
            labelCheck3.Visible = true;
            label5.Visible = true;
        }

        public DeviceAuthTokenResponse GetAccessToken(string code)
        {
            DeviceAuthTokenResponse tokenResponse = null;
            string url = string.Format(TokenUrl, AADId);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            string postData = "grant_type=authorization_code";
            postData += "&client_id=" + HttpUtility.UrlEncode(ClientID);
            postData += "&resource=" + HttpUtility.UrlEncode(GraphResourceUri);
            postData += "&redirect_uri=" + HttpUtility.UrlEncode(RedirectUri);
            postData += "&code=" + HttpUtility.UrlEncode(code);
            byte[] data = encoding.GetBytes(postData);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            WebResponse response = request.GetResponse();
            using (Stream stream = response.GetResponseStream())
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DeviceAuthTokenResponse));
                tokenResponse = (DeviceAuthTokenResponse)ser.ReadObject(stream);
            }

            return tokenResponse;
        }
    }
    [DataContract]
    public class DeviceAuthTokenResponse
    {
        [DataMember(Name = "access_token")]
        public string AccessToken { get; set; }

        [DataMember(Name = "expires_in")]
        public ulong ExpiresIn { get; set; }

        [DataMember(Name = "expires_on")]
        protected ulong expiresOnTimeStamp;

        [DataMember(Name = "id_token")]
        public string IdToken { get; set; }

        [DataMember(Name = "not_before")]
        protected ulong notBeforeTimeStamp;

        [DataMember(Name = "refresh_token")]
        public string RefreshToken { get; set; }

        [DataMember(Name = "resource")]
        public string Resource { get; set; }

        [DataMember(Name = "scope")]
        public string Scope { get; set; }

        [DataMember(Name = "token_type")]
        public string TokenType { get; set; }

        [DataMember(Name = "correlation_id")]
        protected string CorrelationId { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }

        [DataMember(Name = "error_codes")]
        public int[] ErrorCodes { get; set; }

        [DataMember(Name = "error_description")]
        public string ErrorDescription { get; set; }

        [DataMember(Name = "timestamp")]
        protected string TimeStamp { get; set; }

        [DataMember(Name = "trace_id")]
        protected string TraceId { get; set; }

        public DateTime NotBefore
        {
            get
            {
                return (new DateTime(1970, 1, 1, 0, 0, 0, 0) + new TimeSpan(0, 0, Convert.ToInt32(notBeforeTimeStamp)));
            }
        }

        public DateTime ExpiresOn
        {
            get
            {
                return (new DateTime(1970, 1, 1, 0, 0, 0, 0) + new TimeSpan(0, 0, Convert.ToInt32(expiresOnTimeStamp)));
            }
        }

        public bool IsExpired
        {
            get
            {
                return WillExpireIn(0);
            }
        }

        public bool WillExpireIn(int minutes)
        {
            return GenerateTimeStamp(minutes) > expiresOnTimeStamp;
        }

        private static ulong GenerateTimeStamp(int minutes)
        {
            TimeSpan ts = DateTime.UtcNow.AddMinutes(minutes) - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToUInt64(ts.TotalSeconds);
        }
    }
    [DataContract]
    public class AADUser
    {
        [DataMember(Name = "objectId")]
        public string ObjectId { get; set; }

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }

        [DataMember(Name = "userPrincipalName")]
        public string UserPrincipalName { get; set; }

        [DataMember(Name = "userType")]
        public string UserType { get; set; }
    }
    [DataContract]
    public class AADApplication
    {
        [DataMember(Name = "objectId")]
        public string ObjectId { get; set; }

        [DataMember(Name = "appId")]
        public string AppId { get; set; }

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }

        [DataMember(Name = "homepage")]
        public string Homepage { get; set; }

        [DataMember(Name = "identifierUris")]
        public string[] IdentifierUris { get; set; }

        [DataMember(Name = "replyUrls")]
        public string[] ReplyUrls { get; set; }

        [DataMember(Name = "requiredResourceAccess")]
        public AADRequriredResourceAccess[] RequriredResourceAccess { get; set; }

        [DataMember(Name = "keyCredentials")]
        public AADKeyCredential[] KeyCredentials { get; set; }
    }
    [DataContract]
    public class AADKeyCredential
    {
        [DataMember(Name = "customKeyIdentifier")]
        public string CustomKeyIdentifier { get; set; }

        [DataMember(Name = "keyId")]
        public string KeyId { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "usage")]
        public string Usage { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }

        [DataMember(Name = "startDate")]
        public string StartDate { get; set; }

        [DataMember(Name = "endDate")]
        public string EndDate { get; set; }
    }
    [DataContract]
    public class AADRequriredResourceAccess
    {
        [DataMember(Name = "resourceAppId")]
        public string ResourceAppId { get; set; }

        [DataMember(Name = "resourceAccess")]
        public AADResourceAccess[] ResourceAccess { get; set; }
    }
    [DataContract]
    public class AADResourceAccess
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }
    }
    [DataContract]
    public class AADApplicationResult
    {
        [DataMember(Name = "value")]
        public AADApplication[] Applications { get; set; }
    }
}
