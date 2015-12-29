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
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using TrainingPoint;
using TrainingPoint.Models;


namespace TrainingPoint.Controllers
{
    [AuthorizeUser(Roles = "admin, trainer, trainee")]
    public class TrainingController : Controller
    {
        private DataAccess db = new DataAccess();

        // GET: Training
        public async Task<ActionResult> Index()
        {
            //access to training could be granted either directly to the user or to a group that the user is member of. 
            //prepare a single list of objectIds to compare = user's objectId and objectIds of all groups the user is member of.
            List <string> objectIdsToCompare = new List<string>();
            objectIdsToCompare = await GraphUtil.GetMemberGroups(ClaimsPrincipal.Current.Identity as ClaimsIdentity);
            string userObjectId = (ClaimsPrincipal.Current).
                FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            objectIdsToCompare.Add(userObjectId);

            //determine what trainings have been shared with the user and their groups
            var trainingIds = db.AccessControlEntries.Where(a => objectIdsToCompare.Contains(a.PrincipalId.ToString())).Select(a => a.TrainingId).ToList();
            //display the trainings that the user created and the ones shared with the user
            var trainings = db.Trainings.Where(t => trainingIds.Contains(t.Id) || t.CreatedBy == userObjectId).ToList();

            return View(trainings); 
        }

         // GET: Training/Details/5
        public ActionResult Details(int id)
        {
            Training training = db.Trainings
                .Where(t => t.Id == id)
                .Include(t => t.SharedWith)
                .FirstOrDefault();
            if (training == null)
            {
                return HttpNotFound();
            }

            List<string> groupMembership = new List<string>();
            string userObjectId = (System.Security.Claims.ClaimsPrincipal.Current).
                FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            foreach (System.Security.Claims.Claim claim in (System.Security.Claims.ClaimsPrincipal.Current).FindAll("groups"))
                groupMembership.Add(claim.Value);

            List<string> sharedWith = training.SharedWith.Select(s => s.PrincipalId.ToString()).ToList();

            if (training.CreatedBy == userObjectId || groupMembership.Intersect(sharedWith).ToList().Count > 0)
                return View(training);
            else
                return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - this training has not been shared with you." } };
        }

        // GET: Training/Create
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Create()
        {
            return View();
        }

        // POST: Training/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Create([Bind(Include = "Name,Description")] Training training)
        {
            if (ModelState.IsValid)
            {
                training.CreatedBy = (System.Security.Claims.ClaimsPrincipal.Current).
                    FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value;
                training.OrganizationId = (System.Security.Claims.ClaimsPrincipal.Current).
                    FindFirst("http://schemas.microsoft.com/identity/claims/tenantid").Value;
         
                db.Trainings.Add(training);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            return View(training);
        }

        // GET: Training/Edit/5
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Edit(int id)
        {
            Training training = db.Trainings.Find(id);
            if (training == null)
                return HttpNotFound();

            if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                    FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
                return View(training);
            else
                return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can edit training." } };
        }

        // POST: Training/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Edit([Bind(Include = "Id, Name,Description")] Training training)
        {
            if (ModelState.IsValid)
            {
                Training t = db.Trainings.Find(training.Id);
                if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                        FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
                {
                    db.Entry(training).State = EntityState.Modified;
                    db.SaveChanges();
                }
                else
                {
                    return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can edit training." } };
                }
            }
            return RedirectToAction("Index");
        }

        // GET: Training/Delete/5
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Delete(int id)
        {
            Training training = db.Trainings.Find(id);
            
            if (training == null)
                return HttpNotFound();
            
            if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                        FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
                return View(training);
            else
                return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can delete training." } };
        }

        // POST: Training/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult DeleteConfirmed(Guid id)
        {
            Training training = db.Trainings.Find(id);
            if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
            {
                db.Trainings.Remove(training);
                db.SaveChanges();
            }
            else
                return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can delete training." } };
            
            return RedirectToAction("Index");
        }
        [HttpPost]
        [AuthorizeUser(Roles = "admin, trainer")]

        public ActionResult Share(int Id, string UserOrGroupSearchString)
        {
            Training training = db.Trainings
                .Where(t => t.Id == Id)
                .Include(t => t.SharedWith)
                .FirstOrDefault();

            if (training != null)
            {
                if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                    FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
                {
                    string objectIdOfUserOrGroup = GraphUtil.LookupObjectIdOfAADUserOrGroup(UserOrGroupSearchString);
                    if (!string.IsNullOrEmpty(objectIdOfUserOrGroup))
                    {
                        var ace = db.AccessControlEntries.Where(a => a.TrainingId == training.Id && a.PrincipalId == objectIdOfUserOrGroup).FirstOrDefault();
                        if (ace == null)
                        {
                            training.SharedWith.Add(new AccessControlEntry { TrainingId = training.Id, PrincipalId = objectIdOfUserOrGroup });
                            db.SaveChanges();
                        }
                    }
                }
                else
                {
                    return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can share training." } };
                }
            }
            return View("Details", training);
        }
        [HttpPost]
        [AuthorizeUser(Roles = "admin, trainer")]
        public ActionResult Unshare(int Id, int trainingId)
        {
            Training training = db.Trainings
                .Where(t => t.Id == trainingId)
                .Include(t => t.SharedWith)
                .FirstOrDefault();

            if (training != null)
            {
                if (training.CreatedBy == (System.Security.Claims.ClaimsPrincipal.Current).
                    FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier").Value)
                {
                    AccessControlEntry ace = db.AccessControlEntries.Find(Id);
                    db.AccessControlEntries.Remove(ace);
                    db.SaveChanges();

                    training.SharedWith.Remove(ace);
                }
            }
            else
            {
                return new ViewResult { ViewName = "Error", ViewBag = { message = "Unauthorized - only creator can unshare training." } };
            }
            return View("Details", training);
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
