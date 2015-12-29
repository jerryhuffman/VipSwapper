//----------------------------------------------------------------------------------------------
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//----------------------------------------------------------------------------------------------
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using TrainingPoint.Models;

namespace TrainingPoint.Controllers
{
    public class SignUpController : Controller
    {
        private DataAccess db = new DataAccess();
        public ActionResult SignUp()
        {
            return View();
        }
        // POST: /SignUp/SignUp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SignUp([Bind(Include = "Id, Name")] Organization organization)
        {
            // generate a random value to identify the request
            string stateMarker = Guid.NewGuid().ToString();
            // store it in the temporary entry for the tenant, we'll use it later to assess if the request was originated from us
            // this is necessary if we want to prevent attackers from provisioning themselves to access our app without having gone through our onboarding process (e.g. payments, etc)
            organization.Issuer = stateMarker;
            organization.CreatedOn = DateTime.Now;
            db.Organizations.Add(organization);
            db.SaveChanges();

            //create an OAuth2 request, using the web app as the client.
            //this will trigger a consent flow that will provision the app in the target tenant

            string signUpReturnUrl = this.Request.Url.GetLeftPart(UriPartial.Path);
            signUpReturnUrl = signUpReturnUrl.Substring(0, signUpReturnUrl.LastIndexOf('/'));
            signUpReturnUrl += "/ProcessCode";

            string authorizationRequest = String.Format(
                "{0}oauth2/authorize?response_type=code&response_mode=form_post&client_id={1}&resource={2}&redirect_uri={3}&state={4}",
                 string.Format(ConfigurationManager.AppSettings["ida:Authority"], "common"),
                 Uri.EscapeDataString(ConfigurationManager.AppSettings["ida:ClientID"]),
                 Uri.EscapeDataString(ConfigurationManager.AppSettings["ida:GraphAPIIdentifier"]),
                 Uri.EscapeDataString(signUpReturnUrl),
                 Uri.EscapeDataString(stateMarker)
                 );
            //some authorization features require administrator consent
            authorizationRequest += String.Format("&prompt={0}", Uri.EscapeDataString("admin_consent"));
            // send the user to consent
            return new RedirectResult(authorizationRequest);
        }

        // POST: /SignUp/ProcessCode
        public ActionResult ProcessCode(string code, string error, string error_description, string resource, string state)
        {
            // Is this a response to a request we generated? Let's see if the state is carrying an ID we previously saved
            // ---if not, return an error            
            if (db.Organizations.FirstOrDefault(a => a.Issuer == state) == null)
            {
                // TODO: prettify
                return View("Error");
            }
            else
            {
                // ---if the response is indeed from a request we generated
                // ------get a token for Graph API, that will provide us with information abut the caller
                ClientCredential credential = new ClientCredential(ConfigurationManager.AppSettings["ida:ClientID"],
                                                                   ConfigurationManager.AppSettings["ida:Password"]);
                AuthenticationContext authContext = new AuthenticationContext(string.Format(ConfigurationManager.AppSettings["ida:Authority"], "common"));
                AuthenticationResult result = authContext.AcquireTokenByAuthorizationCode(
                    code, new Uri(Request.Url.GetLeftPart(UriPartial.Path)), credential);

                var myTenant = db.Organizations.FirstOrDefault(a => a.Issuer == state);
                // if this was an admin consent, save the tenant
                    // ------read the tenantID out of the Graph token and use it to create the issuer string
                    string issuer = String.Format(ConfigurationManager.AppSettings["ida:Issuer"], result.TenantId);
                    myTenant.Issuer = issuer;

                // remove older, unclaimed entries
                DateTime tenMinsAgo = DateTime.Now.Subtract(new TimeSpan(0, 10, 0)); // workaround for Linq to entities
                var garbage = db.Organizations.Where(a => (!a.Issuer.StartsWith("https") && (a.CreatedOn < tenMinsAgo)));
                foreach (Organization o in garbage)
                    db.Organizations.Remove(o);

                db.SaveChanges();
                // ------return a view claiming success, inviting the user to sign in
                return View();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}