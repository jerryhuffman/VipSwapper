using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Net;
using System.Configuration;
using System.Web;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CloudStack
{
    class CloudStackConsoleInstaller
    {
        static void Main(string[] args)
        {
            CloudStackConsoleInstaller installer = new CloudStackConsoleInstaller();
            Console.Write("---------- CloudStack Installer ----------\n");
            Console.Write("Hello there. First, this installer will download the latest CloudStack binaries from Github." + 
                " Then, it will configure a new website for CloudStack on this IIS server. Finally, it will register the" + 
                " instance of CloudStack with your Azure Active Directory so that it can manage your Azure cloud.\n\n");
            Console.Write("If that sounds good to you");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" please hit enter.");
            Console.ResetColor();
            Console.ReadLine();
            Console.Write("Ok, let's connect this CloudStack instance with your Azure Cloud.\n\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Please enter the id of your Azure subscription that you wish to connect: ");
            Console.ResetColor();
            string subscriptionId = Console.ReadLine();
            string aadId = installer.GetAADForSubscription(subscriptionId);
            DeviceAuthCodeResponse codeResponse = installer.GetDeviceAuthCode(aadId);
            Console.Write("\nGot it. Next, we must create an application registration in your Azure Active Directory" +
                " so that CloudStack can access your Azure cloud. For this, you need to authenticate with the account" +
                " that you use to manage your Azure cloud:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(" use a web browser to open the page {0} and enter the code {1}.", codeResponse.VerificationUrl, codeResponse.UserCode);
            Console.ResetColor();
            Console.Write("\n\nWaiting for you to complete authentication ...");
            DeviceAuthTokenResponse tokenResponse = installer.GetDeviceAuthToken(codeResponse, aadId);
            AADUser user = installer.GetAzureADUser(tokenResponse.AccessToken);
            Console.Write(" Done. \n\nWelcome {0}!", user.DisplayName);
            AADApplication app = installer.RegisterAzureADApplication(tokenResponse.AccessToken, "http://DUGILL-PARALLEL");
            //if (!string.IsNullOrEmpty(tokenResponse.Error) && tokenResponse.Error.Equals("code_expired", StringComparison.InvariantCultureIgnoreCase)) { }
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
        public DeviceAuthCodeResponse GetDeviceAuthCode(string aadId)
        {
            DeviceAuthCodeResponse codeResponse = null;
            string url = string.Format(ConfigurationManager.AppSettings["DeviceCodeUrl"], aadId);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            string postData = "client_id=" + HttpUtility.UrlEncode(ConfigurationManager.AppSettings["ClientID"]);
            postData += "&resource=" + HttpUtility.UrlEncode(ConfigurationManager.AppSettings["GraphResourceUri"]);
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
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DeviceAuthCodeResponse));
                codeResponse = (DeviceAuthCodeResponse)ser.ReadObject(stream);
            }

            return codeResponse;
        }
        public DeviceAuthTokenResponse GetDeviceAuthToken(DeviceAuthCodeResponse codeResponse, string aadId)
        {
            DeviceAuthTokenResponse tokenResponse = null;
            string url = string.Format(ConfigurationManager.AppSettings["TokenUrl"], aadId);

            while (true)
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
                string postData = "grant_type=device_code";
                postData += "&client_id=" + HttpUtility.UrlEncode(ConfigurationManager.AppSettings["ClientID"]);
                postData += "&resource=" + HttpUtility.UrlEncode(ConfigurationManager.AppSettings["GraphResourceUri"]);
                postData += "&code=" + HttpUtility.UrlEncode(codeResponse.DeviceCode);
                byte[] data = encoding.GetBytes(postData);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = data.Length;
                request.UserAgent = "http://www.vipswapper.com/cloudstack";
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                WebResponse response = null;
                try
                {
                    response = request.GetResponse();
                    using (Stream stream = response.GetResponseStream())
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DeviceAuthTokenResponse));
                        tokenResponse = (DeviceAuthTokenResponse)ser.ReadObject(stream);
                    }
                    break;
                }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        using (Stream stream = ((HttpWebResponse)ex.Response).GetResponseStream())
                        {
                            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DeviceAuthTokenResponse));
                            tokenResponse = (DeviceAuthTokenResponse)ser.ReadObject(stream);
                        }
                        if (tokenResponse.Error.Equals("authorization_pending", StringComparison.InvariantCultureIgnoreCase))
                        {
                            Thread.Sleep(codeResponse.Interval * 1000);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            return tokenResponse;
        }
        public AADUser GetAzureADUser(string accessToken)
        {
            AADUser user = null;
            string url = string.Format(ConfigurationManager.AppSettings["GraphUrl"], "me");

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
        public AADApplication RegisterAzureADApplication(string accessToken, string Url)
        {
            AADApplication app = new AADApplication();
            app.DisplayName = "CloudStack-" + Environment.MachineName;
            app.Homepage = Url;
            app.IdentifierUris = new string[1] { Url };
            app.ReplyUrls = new string[3] { Url, "http://localhost", "https://localhost" };
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
                CreateAzureADKeyCredential()
            };

            var existingApp = GetAzureADApplication(accessToken, app.DisplayName);
            if (existingApp != null) RemoveAzureADApplication(accessToken, existingApp.ObjectId);
            CreateAzureADApplication(accessToken, app);
            
            return GetAzureADApplication(accessToken, app.DisplayName);
        }
        private AADApplication GetAzureADApplication(string accessToken, string displayName)
        {
            AADApplication app = null;
            string url = string.Format(ConfigurationManager.AppSettings["GraphUrl"] + "&$filter=displayName eq '{1}'"
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
            string url = string.Format(ConfigurationManager.AppSettings["GraphUrl"], "myorganization/applications/" + objectId);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "DELETE";
            request.Headers.Add(HttpRequestHeader.Authorization, string.Format("{0} {1}", "Bearer", accessToken));
            request.UserAgent = "http://www.vipswapper.com/cloudstack";
            WebResponse response = request.GetResponse();
        }
        private void CreateAzureADApplication(string accessToken, AADApplication app)
        {
            string url = string.Format(ConfigurationManager.AppSettings["GraphUrl"], "myorganization/applications/");
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
        private AADKeyCredential CreateAzureADKeyCredential()
        {
            AADKeyCredential keyCred;

            var certificate = CertHelper.CreateCertificate(new X500DistinguishedName("CN=CloudStack AzureAD KeyCredential"), "CloudStack AzureAD KeyCredential");
            byte[] certData = certificate.Export(X509ContentType.Pfx);
            if(File.Exists(@"X:\Dropbox\Scratch\SelfSigned\CloudStackKeyCredential.pfx"))
                File.Delete(@"X:\Dropbox\Scratch\SelfSigned\CloudStackKeyCredential.pfx");
            File.WriteAllBytes(@"X:\Dropbox\Scratch\SelfSigned\CloudStackKeyCredential.pfx", certData);

            keyCred = new AADKeyCredential();
            keyCred.KeyId = Guid.NewGuid().ToString();
            keyCred.Type = "AsymmetricX509Cert";
            keyCred.Usage = "Verify";
            keyCred.CustomKeyIdentifier = Convert.ToBase64String(certificate.GetCertHash());
            keyCred.Value = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));

            return keyCred;
        }
    }

    [DataContract]
    public class DeviceAuthCodeResponse
    {
        [DataMember(Name = "device_code")]
        public string DeviceCode { get; set; }

        [DataMember(Name = "expires_in")]
        public ulong ExpiresIn { get; set; }

        [DataMember(Name = "interval")]
        public int Interval;

        [DataMember(Name = "message")]
        public string Message;

        [DataMember(Name = "user_code")]
        public string UserCode;

        [DataMember(Name = "verification_url")]
        public string VerificationUrl;
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
