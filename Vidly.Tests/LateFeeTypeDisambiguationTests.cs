using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vidly.Tests
{
    /// <summary>
    /// Regression tests guarding the type-name split between
    /// <see cref="Vidly.Models.LateFeeEstimate"/> (used by the policy
    /// calculator UI and <c>LateFeeService</c>) and
    /// <see cref="Vidly.Services.RentalLateFeeEstimate"/> (used by
    /// <c>RentalReturnService.EstimateCurrentLateFee</c>).
    ///
    /// Before 2026-05-21 both lived under the name <c>LateFeeEstimate</c>
    /// in two different namespaces, which caused CS0104 ambiguity errors
    /// across <c>LateFeesController</c>, <c>LateFeeViewModel</c>, and
    /// every test under <c>LateFeeServiceTests</c>. These tests fail fast
    /// if someone reintroduces the collision.
    /// </summary>
    [TestClass]
    public class LateFeeTypeDisambiguationTests
    {
        [TestMethod]
        public void ModelsLateFeeEstimate_StillExists()
        {
            var t = typeof(Vidly.Models.LateFeeEstimate);
            Assert.IsNotNull(t);
            Assert.AreEqual("Vidly.Models", t.Namespace);
        }

        [TestMethod]
        public void RentalLateFeeEstimate_LivesInServicesNamespace()
        {
            var t = typeof(Vidly.Services.RentalLateFeeEstimate);
            Assert.IsNotNull(t);
            Assert.AreEqual("Vidly.Services", t.Namespace);
        }

        [TestMethod]
        public void ServicesNamespace_DoesNotShipLegacyLateFeeEstimate()
        {
            // Walk the assembly and confirm no other type still uses the
            // shorter "LateFeeEstimate" name inside Vidly.Services.
            var asm = typeof(Vidly.Services.RentalLateFeeEstimate).Assembly;
            var collisions = 0;
            foreach (var t in asm.GetTypes())
            {
                if (t.Namespace == "Vidly.Services" && t.Name == "LateFeeEstimate")
                    collisions++;
            }

            Assert.AreEqual(0, collisions,
                "Vidly.Services must not declare a type named 'LateFeeEstimate' " +
                "— it collides with Vidly.Models.LateFeeEstimate and breaks the " +
                "LateFeesController + LateFeeService consumers.");
        }

        [TestMethod]
        public void RentalLateFeeEstimate_HasRentalShapedFields()
        {
            // Spot-check a few of the rental-specific fields the return
            // flow depends on; if someone reshapes the type, the
            // self-service estimator surface should be revisited too.
            var t = typeof(Vidly.Services.RentalLateFeeEstimate);
            Assert.IsNotNull(t.GetProperty("RentalId"), "RentalId expected");
            Assert.IsNotNull(t.GetProperty("MovieName"), "MovieName expected");
            Assert.IsNotNull(t.GetProperty("EstimatedFee"), "EstimatedFee expected");
            Assert.IsNotNull(t.GetProperty("FeeCapped"), "FeeCapped expected");
            Assert.IsNotNull(t.GetProperty("MembershipDiscount"), "MembershipDiscount expected");
        }

        [TestMethod]
        public void ModelsLateFeeEstimate_HasPolicyShapedFields()
        {
            // The Vidly.Models flavor is policy-calculator shaped.
            var t = typeof(Vidly.Models.LateFeeEstimate);
            Assert.IsNotNull(t.GetProperty("PolicyName"), "PolicyName expected");
            Assert.IsNotNull(t.GetProperty("Strategy"), "Strategy expected");
            Assert.IsNotNull(t.GetProperty("Fee"), "Fee expected");
            Assert.IsNotNull(t.GetProperty("ChargeableDays"), "ChargeableDays expected");
            Assert.IsNotNull(t.GetProperty("WasCapped"), "WasCapped expected");
            Assert.IsNotNull(t.GetProperty("TierBreakdowns"), "TierBreakdowns expected");
        }

        [TestMethod]
        public void TwoEstimateTypes_AreDistinct()
        {
            Assert.AreNotSame(
                typeof(Vidly.Models.LateFeeEstimate),
                typeof(Vidly.Services.RentalLateFeeEstimate),
                "These must remain two distinct types.");
        }
    }
}
