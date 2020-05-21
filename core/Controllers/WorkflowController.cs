﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using puck.core.Helpers;
using System.Reflection;
using System.IO;
using Newtonsoft.Json;
using puck.core.Abstract;
using puck.core.Constants;
using puck.core.Base;
using puck.core.Entities;
using puck.core.Models;
using StackExchange.Profiling;
using System.Threading.Tasks;
using puck.core.State;
using puck.core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using puck.core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using puck.core.Concrete;
using Microsoft.Extensions.Caching.Memory;
using LinqKit;
using System.Threading;

namespace puck.core.Controllers
{
    [Area("puck")]
    public class WorkflowController : BaseController
    {
        private static SemaphoreSlim slock1 = new SemaphoreSlim(1);
        I_Content_Indexer indexer;
        I_Content_Searcher searcher;
        I_Log log;
        I_Puck_Repository repo;
        RoleManager<PuckRole> roleManager;
        UserManager<PuckUser> userManager;
        SignInManager<PuckUser> signInManager;
        I_Content_Service contentService;
        I_Api_Helper apiHelper;
        IHostEnvironment env;
        IConfiguration config;
        IMemoryCache cache;
        public WorkflowController(I_Api_Helper ah, I_Content_Service cs, I_Content_Indexer i, I_Content_Searcher s, I_Log l, I_Puck_Repository r, RoleManager<PuckRole> rm, UserManager<PuckUser> um, SignInManager<PuckUser> sm, IHostEnvironment env, IConfiguration config, IMemoryCache cache)
        {
            this.indexer = i;
            this.searcher = s;
            this.log = l;
            this.repo = r;
            this.roleManager = rm;
            this.userManager = um;
            this.signInManager = sm;
            this.contentService = cs;
            this.apiHelper = ah;
            this.env = env;
            this.config = config;
            this.cache = cache;
            StateHelper.SetFirstRequestUrl();
            SyncIfNecessary();
        }

        [HttpPost]
        [Authorize(Roles = PuckRoles.Puck, AuthenticationSchemes = Mvc.AuthenticationScheme)]
        public async Task<IActionResult> Create(PuckWorkflowItem model) {
            var success = false;
            var message = "";

            try
            {
                if (!ModelState.IsValid)
                {
                    message = string.Join(",", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
                    return Json(new { success = success, message = message });
                }
                await slock1.WaitAsync();

                var existingItems = repo.GetPuckWorkflowItem().Where(x => x.ContentId == model.ContentId && x.Variant == model.Variant && !x.Complete).ToList();
                existingItems.ForEach(x => { x.Complete = true; x.CompleteDate = DateTime.Now; });

                repo.AddPuckWorkflowItem(model);
                repo.SaveChanges();

                success = true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                log.Log(ex);
            }
            finally {
                slock1.Release();
            }

            return Json(new {success=success,message=message });
        }

        [HttpPost]
        [Authorize(Roles = PuckRoles.Puck, AuthenticationSchemes = Mvc.AuthenticationScheme)]
        public async Task<IActionResult> Complete(Guid contentId,string variant)
        {
            var success = false;
            var message = "";

            try
            {
                var existingItems = repo.GetPuckWorkflowItem().Where(x => x.ContentId == contentId && x.Variant == variant && !x.Complete).ToList();
                existingItems.ForEach(x => { x.Complete = true; x.CompleteDate = DateTime.Now; });

                repo.SaveChanges();

                success = true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                log.Log(ex);
            }

            return Json(new { success = success, message = message });
        }

        [HttpPost]
        [Authorize(Roles = PuckRoles.Puck, AuthenticationSchemes = Mvc.AuthenticationScheme)]
        public async Task<IActionResult> Index()
        {
            var user = await userManager.GetUserAsync(User);

            var userGroups = user.PuckUserGroups.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var predicate = PredicateBuilder.New<PuckWorkflowItem>();

            foreach (var group in userGroups) {
                predicate = predicate.Or(x=>x.Group.Equals(group));
            }

            predicate.Or(x => x.Assignees.Contains(user.Email));

            var model = repo.GetPuckWorkflowItem().AsExpandable().Where(predicate).ToList();

            var ids = model.Select(x => x.ContentId);

            var names = repo.GetPuckRevision().Where(x => x.Current && ids.Contains(x.Id)).Select(x => new PuckRevision { NodeName = x.NodeName, Id=x.Id,Variant=x.Variant }).ToList();

            var nameDict = new Dictionary<Guid, string>();

            names.ForEach(x=>nameDict[x.Id]=x.NodeName+$" - {x.Variant}");

            ViewBag.Names = nameDict;

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = PuckRoles.Puck, AuthenticationSchemes = Mvc.AuthenticationScheme)]
        public async Task<IActionResult> Lock(Guid contentId, string variant,string until)
        {
            var success = false;
            var message = "";

            try
            {
                var existingItems = repo.GetPuckWorkflowItem().Where(x => x.ContentId == contentId && x.Variant == variant && !x.Complete).ToList();

                DateTime lockedUntil = DateTime.Now;

                switch (until) {
                    case "10 mins":
                        lockedUntil = DateTime.Now.AddMinutes(10);
                        break;
                    case "30 mins":
                        lockedUntil = DateTime.Now.AddMinutes(30);
                        break;
                    case "1 hour":
                        lockedUntil = DateTime.Now.AddHours(1);
                        break;
                    case "2 hours":
                        lockedUntil = DateTime.Now.AddHours(2);
                        break;
                    case "5 hours":
                        lockedUntil = DateTime.Now.AddHours(5);
                        break;
                    case "8 hour":
                        lockedUntil = DateTime.Now.AddHours(8);
                        break;
                }
                
                existingItems.ForEach(x => { x.LockedUntil=lockedUntil; x.LockedBy = User.Identity.Name; });

                repo.SaveChanges();

                success = true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                log.Log(ex);
            }

            return Json(new { success = success, message = message });
        }

    }
}