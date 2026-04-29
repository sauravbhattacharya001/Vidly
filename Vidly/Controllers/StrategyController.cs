using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Controllers
{
    /// <summary>
    /// Strategy Simulator — autonomous what-if engine for business decisions.
    /// Simulates pricing changes, marketing campaigns, inventory adjustments,
    /// and loyalty programs, then projects multi-week impact.
    /// </summary>
    public class StrategyController : Controller
    {
        private readonly StrategySimulatorService _simulator;

        public StrategyController()
        {
            var rentals = new InMemoryRentalRepository();
            var movies = new InMemoryMovieRepository();
            var customers = new InMemoryCustomerRepository();
            _simulator = new StrategySimulatorService(rentals, movies, customers, new SystemClock());
        }

        public StrategyController(StrategySimulatorService simulator)
        {
            _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        }

        // GET: /Strategy
        public ActionResult Index()
        {
            var recommendations = _simulator.GetRecommendations();
            var history = _simulator.GetHistory();
            return Json(new
            {
                engine = "Strategy Simulator v1.0",
                description = "Autonomous what-if scenario engine for business decisions",
                capabilities = new[]
                {
                    "Simulate pricing changes with elasticity modeling",
                    "Project marketing campaign ROI with diminishing returns",
                    "Model inventory expansion impact on utilization",
                    "Forecast churn reduction from loyalty programs",
                    "Risk assessment with mitigation recommendations",
                    "Week-by-week projections with S-curve ramp modeling",
                    "Head-to-head scenario comparison",
                    "Autonomous strategy recommendations based on store state"
                },
                recommendations = recommendations,
                recentSimulations = history.TakeLast(5),
                endpoints = new
                {
                    simulate = "POST /Strategy/Simulate",
                    recommendations = "GET /Strategy/Recommendations",
                    history = "GET /Strategy/History",
                    detail = "GET /Strategy/Detail/{id}",
                    compare = "POST /Strategy/Compare",
                    presets = "GET /Strategy/Presets"
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: /Strategy/Recommendations
        public ActionResult Recommendations()
        {
            var recs = _simulator.GetRecommendations();
            return Json(new
            {
                generated = DateTime.Now,
                count = recs.Count,
                recommendations = recs
            }, JsonRequestBehavior.AllowGet);
        }

        // POST: /Strategy/Simulate
        [HttpPost]
        public ActionResult Simulate(StrategyScenario scenario)
        {
            if (scenario == null)
                return Json(new { error = "Scenario is required" });

            try
            {
                var result = _simulator.Simulate(scenario);
                return Json(new
                {
                    success = true,
                    simulation = result
                });
            }
            catch (ArgumentException ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // GET: /Strategy/History
        public ActionResult History()
        {
            var history = _simulator.GetHistory();
            return Json(new
            {
                count = history.Count,
                simulations = history.Select(h => new
                {
                    h.Id,
                    h.Scenario.Name,
                    h.Verdict.Grade,
                    h.Comparison.NetRevenueImpact,
                    h.RiskAssessment.RiskLevel,
                    h.SimulatedAt
                })
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: /Strategy/Detail/5
        public ActionResult Detail(int id)
        {
            var result = _simulator.GetById(id);
            if (result == null)
                return Json(new { error = "Simulation not found" }, JsonRequestBehavior.AllowGet);

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        // POST: /Strategy/Compare
        [HttpPost]
        public ActionResult Compare(StrategyScenario scenarioA, StrategyScenario scenarioB)
        {
            if (scenarioA == null || scenarioB == null)
                return Json(new { error = "Both scenarios are required" });

            var comparison = _simulator.CompareScenarios(scenarioA, scenarioB);
            return Json(new
            {
                success = true,
                comparison = comparison
            });
        }

        // GET: /Strategy/Presets
        public ActionResult Presets()
        {
            var presets = new[]
            {
                new
                {
                    name = "Summer Blockbuster Push",
                    description = "Capitalize on summer demand with new releases and targeted marketing",
                    scenario = new StrategyScenario
                    {
                        Name = "Summer Blockbuster Push",
                        PriceChangePercent = 5,
                        MarketingBudgetMultiplier = 1.8,
                        NewInventoryPercent = 25,
                        HorizonWeeks = 12,
                        Strategies = new List<StrategyType> { StrategyType.PremiumContent, StrategyType.MarketingPush, StrategyType.InventoryExpansion }
                    }
                },
                new
                {
                    name = "Budget-Friendly Recovery",
                    description = "Price cuts to recover lost customers during a downturn",
                    scenario = new StrategyScenario
                    {
                        Name = "Budget-Friendly Recovery",
                        PriceChangePercent = -25,
                        MarketingBudgetMultiplier = 1.3,
                        LoyaltyBoostPercent = 20,
                        HorizonWeeks = 8,
                        Strategies = new List<StrategyType> { StrategyType.PriceReduction, StrategyType.LoyaltyEnhancement, StrategyType.PersonalizedOutreach }
                    }
                },
                new
                {
                    name = "Premium Pivot",
                    description = "Shift to premium content and higher prices for better margins",
                    scenario = new StrategyScenario
                    {
                        Name = "Premium Pivot",
                        PriceChangePercent = 15,
                        NewInventoryPercent = 15,
                        HorizonWeeks = 16,
                        Strategies = new List<StrategyType> { StrategyType.PriceIncrease, StrategyType.PremiumContent }
                    }
                },
                new
                {
                    name = "Loyalty Fortress",
                    description = "Maximum retention — keep every customer at all costs",
                    scenario = new StrategyScenario
                    {
                        Name = "Loyalty Fortress",
                        PriceChangePercent = -10,
                        LoyaltyBoostPercent = 50,
                        MarketingBudgetMultiplier = 1.1,
                        HorizonWeeks = 12,
                        Strategies = new List<StrategyType> { StrategyType.LoyaltyEnhancement, StrategyType.PersonalizedOutreach, StrategyType.BundleDeals }
                    }
                },
                new
                {
                    name = "Rapid Expansion",
                    description = "Aggressive growth through inventory and marketing",
                    scenario = new StrategyScenario
                    {
                        Name = "Rapid Expansion",
                        NewInventoryPercent = 40,
                        MarketingBudgetMultiplier = 2.5,
                        PriceChangePercent = -5,
                        HorizonWeeks = 20,
                        Strategies = new List<StrategyType> { StrategyType.InventoryExpansion, StrategyType.MarketingPush, StrategyType.StoreHoursExpansion }
                    }
                }
            };

            return Json(new { presets }, JsonRequestBehavior.AllowGet);
        }
    }
}
