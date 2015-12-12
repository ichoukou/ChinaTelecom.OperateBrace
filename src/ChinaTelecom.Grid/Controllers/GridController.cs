﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Authorization;
using ChinaTelecom.Grid.Models;
using ChinaTelecom.Grid.ViewModels;

namespace ChinaTelecom.Grid.Controllers
{
    [Authorize]
    public class GridController : BaseController
    {
        public async Task<IActionResult> Index()
        {
            ViewBag.Areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateEstate(string title, double lon, double lat, string rules, string area)
        {
            var estate = new Estate
            {
                Area = area,
                Lat = lat,
                Lon = lon,
                Title = title
            };
            DB.Estates.Add(estate);
            if (!string.IsNullOrEmpty(rules))
                foreach (var x in rules.Split('\n'))
                    DB.EstateRules.Add(new EstateRule { Rule = x, EstateId = estate.Id });
            DB.SaveChanges();
            return Content("ok");
        }

        [HttpPost]
        public IActionResult ModifyPosition(Guid id, double lon, double lat)
        {
            var estate = DB.Estates.Where(x => x.Id == id).Single();
            estate.Lon = lon;
            estate.Lat = lat;
            DB.SaveChanges();
            return Content("ok");
        }

        [HttpGet]
        public IActionResult GetEstates(double left, double right, double top, double bottom)
        {
            var estates = DB.Estates.Where(x => x.Lon >= left && x.Lon <= right && x.Lat <= top && x.Lat >= bottom);
            foreach(var x in estates)
            {
                var tmp = DB.Houses
                    .Include(y => y.Building)
                    .Where(y => y.Building.EstateId == x.Id && y.HouseStatus == HouseStatus.中国电信);
                x.TotalCTUsers = tmp.Count();
                x.TotalInUsingUsers = tmp.Where(y => y.ServiceStatus == ServiceStatus.在用).Count();
                x.TotalNonCTUsers = DB.Houses
                    .Include(y => y.Building)
                    .Where(y => y.Building.EstateId == x.Id && y.HouseStatus != HouseStatus.中国电信 && y.HouseStatus != HouseStatus.未装机)
                    .Count();
                x.AddedUsers = DB.Houses
                    .Include(y => y.Building)
                    .Where(y => y.Building.EstateId == x.Id && y.HouseStatus != HouseStatus.中国电信 && y.ServiceStatus == ServiceStatus.在用 && y.IsStatusChanged)
                    .Count();
                x.LeftUsers = DB.Houses
                    .Include(y => y.Building)
                    .Where(y => y.Building.EstateId == x.Id && y.HouseStatus != HouseStatus.中国电信 && y.ServiceStatus != ServiceStatus.在用 && y.IsStatusChanged)
                    .Count();
                if (x.TotalCTUsers == 0)
                    x.UsingRate = 0;
                else
                    x.UsingRate = (double)x.TotalInUsingUsers / (double)x.TotalCTUsers;
            }
            return Json(estates);
        }

        [HttpGet]
        public async Task<IActionResult> Area(bool? raw)
        {
            IEnumerable<Estate> tmp = DB.Estates
                .Include(x => x.Buildings)
                .ThenInclude(x => x.Houses);
            if (!User.IsInRole("系统管理员"))
            {
                var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                tmp = tmp.Where(x => areas.Contains(x.Area));
            }
            var ret = tmp.OrderBy(x => x.Area)
                .GroupBy(x => x.Area)
                .Select(x => new Area
                {
                    Id = x.Key,
                    Count = x.Count(),
                    CTUsers = x.Sum(y => y.Buildings.Sum(z => z.Houses.Where(a => a.HouseStatus == HouseStatus.中国电信).Count())),
                    CTInUsingUsers = x.Sum(y => y.Buildings.Sum(z => z.Houses.Where(a => a.HouseStatus == HouseStatus.中国电信 && a.ServiceStatus == ServiceStatus.在用).Count())),
                    NonCTUsers = x.Sum(y => y.Buildings.Sum(z => z.Houses.Where(a => a.HouseStatus != HouseStatus.中国电信 && a.HouseStatus != HouseStatus.未装机).Count())),
                    AddedUsers = x.Sum(y => y.Buildings.Sum(z => z.Houses.Where(a => a.HouseStatus == HouseStatus.中国电信 && a.IsStatusChanged && a.ServiceStatus == ServiceStatus.在用).Count())),
                    LeftUsers = x.Sum(y => y.Buildings.Sum(z => z.Houses.Where(a => a.HouseStatus == HouseStatus.中国电信 && a.IsStatusChanged && a.ServiceStatus != ServiceStatus.在用).Count())),
                    Lon = x.FirstOrDefault() == null ? null : (double?)x.FirstOrDefault().Lon,
                    Lat = x.FirstOrDefault() == null ? null : (double?)x.FirstOrDefault().Lat
                });
            if (raw.HasValue && raw.Value)
                return XlsView(ret.ToList(), "Area.xls", "ExportArea");
            else
                return PagedView(ret);
        }

        [HttpGet]
        public async Task<IActionResult> Estate(string Area, string Title, bool? raw)
        {
            IEnumerable<Estate> ret = DB.Estates
                .Include(x => x.Buildings)
                .ThenInclude(x => x.Houses)
                .Include(x => x.Rules);
            if (!User.IsInRole("系统管理员"))
            {
                var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                ret = ret.Where(x => areas.Contains(x.Area));
            }
            if (!string.IsNullOrEmpty(Area))
                ret = ret.Where(x => x.Area == Area);
            if (!string.IsNullOrEmpty(Title))
                ret = ret.Where(x => x.Title.Contains(Title) || Title.Contains(x.Title));
            ret = ret.OrderBy(x => x.Area);
            if (raw.HasValue && raw.Value)
                return XlsView(ret.ToList(), "Estate.xls", "ExportEstate");
            else
                return PagedView(ret);
        }

        [HttpGet]
        public IActionResult Show(Guid id)
        {
            ViewBag.EstateTitle = DB.Estates
                .Where(x => x.Id == id)
                .Single()
                .Title;
            var ret = DB.Buildings
                .Include(x => x.Houses)
                .Where(x => x.EstateId == id)
                .OrderBy(x => x.Title)
                .ToList();
            var accounts = DB.Houses
                .Select(x => x.Account)
                .ToList();
            var rules = DB.EstateRules
                .Where(x => x.EstateId == id)
                .Select(x => x.Rule)
                .ToList();

            // 查找没有对应至楼宇中的地址信息
            var pendingAddress = new List<Record>();
            foreach (var x in rules)
            {
                pendingAddress.AddRange(DB.Records
                    .Where(a => !DB.Houses
                    .Select(b => b.Account)
                    .Contains(a.Account))
                    .Where(a => a.ImplementAddress.Contains(x) || a.StandardAddress.Contains(x))
                    );
            }
            pendingAddress = pendingAddress.OrderByDescending(x => x.ImportedTime).DistinctBy(x => x.Account).ToList();

            // 尝试根据规则进行对应
            foreach(var x in pendingAddress)
            {
                try
                {
                    var building = Lib.AddressAnalyser.GetBuildingNumber(x.ImplementAddress);
                    var unit = Lib.AddressAnalyser.GetUnit(x.ImplementAddress);
                    var layer = Lib.AddressAnalyser.GetLayer(x.ImplementAddress);
                    var door = Lib.AddressAnalyser.GetDoor(x.ImplementAddress);
                    if (ret.Any(a => a.Title == building))
                    {
                        var _building = ret.Where(a => a.Title == building).Single();
                        if (unit > 0 && unit < _building.Units && layer >= _building.BottomLayers && layer <= _building.TopLayers && door > 0 && door < _building.Doors)
                        {
                            var prev = DB.Records
                                .Where(a => a.Account == x.Account)
                                .Where(a => a.ImportedTime < x.ImportedTime)
                                .LastOrDefault();
                            DB.Houses.Add(new House
                            {
                                Account = x.Account,
                                ServiceStatus = x.Status,
                                HouseStatus = HouseStatus.中国电信,
                                Unit = unit.Value,
                                Layer = layer.Value,
                                Door = door.Value,
                                LastUpdate = x.ImportedTime,
                                Phone = x.Phone,
                                FullName = x.CustomerName,
                                BuildingId = _building.Id,
                                IsStatusChanged = prev == null ? true : prev.Status == x.Status ? false : true
                            });
                            DB.SaveChanges();
                            pendingAddress.Remove(x);
                        }
                    }
                }
                catch
                {
                }
            }
            ViewBag.PendingAddresses = pendingAddress;
            return View(ret);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveBuilding(Guid id)
        {
            var building = DB.Buildings
                .Include(x => x.Estate)
                .Where(x => x.Id == id)
                .Single();
            if (!User.IsInRole("系统管理员"))
            {
                var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                if (!areas.Contains(building.Estate.Area))
                {
                    return Prompt(x =>
                    {
                        x.Title = "删除失败";
                        x.Details = "您无权删除该楼座";
                    });
                }
            }
            DB.Buildings.Remove(building);
            DB.SaveChanges();
            return RedirectToAction("Show", "Grid", new { id = building.EstateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBuilding(Guid id, Building Model)
        {
            var estate = DB.Estates.Where(x => x.Id == id).Single();
            if (!User.IsInRole("系统管理员"))
            {
                var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                if (!areas.Contains(estate.Area))
                {
                    return Prompt(x =>
                    {
                        x.Title = "创建失败";
                        x.Details = "您没有该片区的管辖权";
                    });
                }
            }
            Model.Id = Guid.NewGuid();
            Model.EstateId = id;
            DB.Buildings.Add(Model);
            DB.SaveChanges();
            return RedirectToAction("Show", "Grid", new { id = id });
        }
        
        [HttpGet]
        public IActionResult Building(Guid id)
        {
            var ret = DB.Buildings
                .Include(x => x.Estate)
                .Include(x => x.Houses)
                .Where(x => x.Id == id)
                .Single();
            var accounts = DB.Houses
                .Select(x => x.Account)
                .ToList();
            var rules = DB.EstateRules
                .Where(x => x.EstateId == id)
                .Select(x => x.Rule)
                .ToList();

            // 查找没有对应至楼宇中的地址信息
            var pendingAddress = new List<Record>();
            foreach (var x in rules)
            {
                pendingAddress.AddRange(DB.Records
                    .Where(a => !DB.Houses
                    .Select(b => b.Account)
                    .Contains(a.Account))
                    .Where(a => a.ImplementAddress.Contains(x) || a.StandardAddress.Contains(x))
                    );
            }
            pendingAddress = pendingAddress.OrderByDescending(x => x.ImportedTime).DistinctBy(x => x.Account).ToList();

            // 尝试根据规则进行对应
            foreach (var x in pendingAddress)
            {
                try
                {
                    var building = Lib.AddressAnalyser.GetBuildingNumber(x.ImplementAddress);
                    var unit = Lib.AddressAnalyser.GetUnit(x.ImplementAddress);
                    var layer = Lib.AddressAnalyser.GetLayer(x.ImplementAddress);
                    var door = Lib.AddressAnalyser.GetDoor(x.ImplementAddress);
                    if (ret.Title == building)
                    {
                        var _building = ret;
                        if (unit > 0 && unit < _building.Units && layer >= _building.BottomLayers && layer <= _building.TopLayers && door > 0 && door < _building.Doors)
                        {
                            var prev = DB.Records
                                .Where(a => a.Account == x.Account)
                                .Where(a => a.ImportedTime < x.ImportedTime)
                                .LastOrDefault();
                            DB.Houses.Add(new House
                            {
                                Account = x.Account,
                                ServiceStatus = x.Status,
                                HouseStatus = HouseStatus.中国电信,
                                Unit = unit.Value,
                                Layer = layer.Value,
                                Door = door.Value,
                                LastUpdate = x.ImportedTime,
                                Phone = x.Phone,
                                FullName = x.CustomerName,
                                BuildingId = _building.Id,
                                IsStatusChanged = prev == null ? true : prev.Status == x.Status ? false : true
                            });
                            DB.SaveChanges();
                            pendingAddress.Remove(x);
                        }
                    }
                }
                catch
                {
                }
            }
            ViewBag.PendingAddresses = pendingAddress;
            return View(ret);
        }

        [HttpPost]
        public async Task<IActionResult> Transfer(string Account, Guid? BuildingId, int? Unit, int? Layer, int? Door)
        {
            if (Unit == null || Layer == null || Door == null || BuildingId == null)
            {
                var house = DB.Houses
                    .Include(x => x.Building)
                    .ThenInclude(x => x.Estate)
                    .Where(x => x.Account == Account)
                    .Single();
                if (!User.IsInRole("系统管理员"))
                {
                    var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                    if (!areas.Contains(house.Building.Estate.Area))
                    {
                        return Prompt(x =>
                        {
                            x.Title = "操作失败";
                            x.Details = "您没有权限将该楼座中的用户移出！";
                        });
                    }
                }
                DB.Houses.Remove(house);
            }
            else
            {
                var building = DB.Buildings
                    .Include(x => x.Estate)
                    .Where(x => x.Id == BuildingId.Value)
                    .Single();

                if (!User.IsInRole("系统管理员"))
                {
                    var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                    if (!areas.Contains(building.Estate.Area))
                    {
                        return Prompt(x =>
                        {
                            x.Title = "操作失败";
                            x.Details = "您没有权限向该楼座添加用户！";
                        });
                    }
                }

                var record = DB.Records.Where(x => x.Account == Account).LastOrDefault();
                var houses = DB.Houses.Where(x => x.Account == Account).SingleOrDefault();
                if (houses == null)
                {
                    DB.Houses.Add(new House
                    {
                        Account = Account,
                        BuildingId = BuildingId.Value,
                        Door = Door.Value,
                        Layer = Layer.Value,
                        Unit = Unit.Value,
                        FullName = record.CustomerName,
                        HouseStatus = HouseStatus.中国电信,
                        IsStatusChanged = true,
                        Phone = record.Phone,
                        LastUpdate = DateTime.Now,
                        ServiceStatus = record.Status
                    });
                }
                else
                {
                    houses.Unit = Unit.Value;
                    houses.Layer = Layer.Value;
                    houses.Door = Door.Value;
                }
            }
            DB.SaveChanges();
            return Content("ok");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHouse(Guid id, int unit, int layer, int door, string account, HouseStatus provider, string fullname, string phone, [FromServices] string Referer)
        {
            var building = DB.Buildings
                    .Include(x => x.Estate)
                    .Where(x => x.Id == id)
                    .Single();

            if (!User.IsInRole("系统管理员"))
            {
                var areas = (await UserManager.GetClaimsAsync(User.Current)).Where(x => x.Type == "管辖片区").Select(x => x.Value).ToList();
                if (!areas.Contains(building.Estate.Area))
                {
                    return Prompt(x =>
                    {
                        x.Title = "操作失败";
                        x.Details = "您没有权限向该楼座添加用户！";
                    });
                }
            }

            if (provider == HouseStatus.中国电信)
            {
                var record = DB.Records.Where(x => x.Account == account).LastOrDefault();
                if (record == null)
                    return Prompt(x =>
                    {
                        x.Title = "操作失败";
                        x.Details = "没有找到该接入号对应的用户！";
                        x.StatusCode = 404;
                    });

                DB.Houses.Add(new House
                {
                    BuildingId = id,
                    Account = account,
                    Unit = unit,
                    Layer = layer,
                    Door = door,
                    HouseStatus = HouseStatus.中国电信,
                    LastUpdate = DateTime.Now,
                    IsStatusChanged = true,
                    Phone = record.Phone,
                    ServiceStatus = record.Status,
                    FullName = record.CustomerName
                });
            }
            else
            {
                DB.Houses.Add(new House
                {
                    BuildingId = id,
                    Account = account,
                    Unit = unit,
                    Layer = layer,
                    Door = door,
                    HouseStatus = HouseStatus.中国电信,
                    LastUpdate = DateTime.Now,
                    IsStatusChanged = true,
                    Phone = phone,
                    ServiceStatus = ServiceStatus.未知,
                    FullName = fullname
                });
            }
            DB.SaveChanges();
            return Redirect(Referer);
        }
    }
}