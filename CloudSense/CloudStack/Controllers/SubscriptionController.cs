using CloudStack.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Claims;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace CloudStack.Controllers
{
    public class SubscriptionController : Controller
    {
        private DataAccess db = new DataAccess();

        public ActionResult Connect([Bind(Include = "Id")] Subscription subscription)
        {
            if (ModelState.IsValid)
            {
                AzureResourceManagerUtil.GrantRoleToServicePrincipalOnSubscription(ConfigurationManager.AppSettings["ObjectId"], 
                    subscription.Id, ConfigurationManager.AppSettings["AADId"]);
                if (AzureResourceManagerUtil.ServicePrincipalHasReadAccessToSubscription(subscription.Id, ConfigurationManager.AppSettings["AADId"]))
                {
                    subscription.ConnectedBy = (System.Security.Claims.ClaimsPrincipal.Current).FindFirst(ClaimTypes.Name).Value;
                    subscription.ConnectedOn = DateTime.Now;

                    db.Subscriptions.Add(subscription);
                    db.SaveChanges();
                }
            }

            return RedirectToAction("Index", "Home");
        }
        public ActionResult Disconnect([Bind(Include = "Id")] Subscription subscription)
        {
            if (ModelState.IsValid)
            {
                AzureResourceManagerUtil.RevokeRoleFromServicePrincipalOnSubscription(ConfigurationManager.AppSettings["ObjectId"], 
                    subscription.Id, ConfigurationManager.AppSettings["AADId"]);

                Subscription s = db.Subscriptions.Find(subscription.Id);
                if (s != null)
                {
                    db.Subscriptions.Remove(s);
                    db.SaveChanges();
                }
                
            }

            return RedirectToAction("Index", "Home");
        }
        public ActionResult RepairAccess([Bind(Include = "Id")] Subscription subscription)
        {
            if (ModelState.IsValid)
            {
                AzureResourceManagerUtil.RevokeRoleFromServicePrincipalOnSubscription(ConfigurationManager.AppSettings["ObjectId"], subscription.Id, ConfigurationManager.AppSettings["AADId"]);
                AzureResourceManagerUtil.GrantRoleToServicePrincipalOnSubscription(ConfigurationManager.AppSettings["ObjectId"], subscription.Id, ConfigurationManager.AppSettings["AADId"]);
            }

            return RedirectToAction("Index", "Home");
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