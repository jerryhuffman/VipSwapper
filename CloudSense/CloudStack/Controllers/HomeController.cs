using CloudStack.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Mvc;

namespace CloudStack.Controllers
{
    public class HomeController : Controller
    {
        private DataAccess db = new DataAccess();

        public ActionResult Index()
        {
            HomeIndexViewModel model = null;

            if (ClaimsPrincipal.Current.Identity.IsAuthenticated)
            {
                model = new HomeIndexViewModel();
                model.UserSubscriptions = new Dictionary<string, Subscription>();
                model.UserCanManageAccessForSubscriptions = new List<string>();

                var subscriptions = AzureResourceManagerUtil.GetUserSubscriptions(ConfigurationManager.AppSettings["AADId"]);

                if (subscriptions != null)
                {
                    foreach (var subscription in subscriptions)
                    {

                        Subscription s = db.Subscriptions.Find(subscription.Id);
                        if (s != null)
                        {
                            subscription.IsConnected = true;
                            subscription.ConnectedOn = s.ConnectedOn;
                            subscription.ConnectedBy = s.ConnectedBy;
                            subscription.AzureAccessNeedsToBeRepaired = !AzureResourceManagerUtil.ServicePrincipalHasReadAccessToSubscription
                                (subscription.Id, ConfigurationManager.AppSettings["AADId"]);
                        }
                        else
                        {
                            subscription.IsConnected = false;
                        }

                        model.UserSubscriptions.Add(subscription.Id, subscription);
                        if (AzureResourceManagerUtil.UserCanManageAccessForSubscription(subscription.Id, ConfigurationManager.AppSettings["AADId"]))
                            model.UserCanManageAccessForSubscriptions.Add(subscription.Id);
                    }
                }
            }
            return View(model);
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
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