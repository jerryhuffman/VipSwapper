using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CloudStack.Models
{
    public class HomeIndexViewModel
    {
        public Dictionary<string, Subscription> UserSubscriptions { get; set; }
        public List<string> UserCanManageAccessForSubscriptions { get; set; }
    }
}