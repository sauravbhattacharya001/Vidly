using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;

namespace Vidly.Tests
{
    [TestClass]
    public class CustomerModelTests
    {
        /// <summary>
        /// Validates that the Customer model enforces the [Required] attribute on Name.
        /// </summary>
        [TestMethod]
        public void Customer_NameIsRequired()
        {
            var customer = new Customer { Id = 1, Name = null };
            var results = ValidateModel(customer);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Expected a validation error on 'Name' when it is null.");
        }

        /// <summary>
        /// Ensures that a customer with a valid name passes validation.
        /// </summary>
        [TestMethod]
        public void Customer_ValidName_PassesValidation()
        {
            var customer = new Customer { Id = 1, Name = "John Doe" };
            var results = ValidateModel(customer);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Name")),
                "A valid customer name should not produce validation errors.");
        }

        /// <summary>
        /// Ensures that Name exceeding 255 characters fails validation.
        /// </summary>
        [TestMethod]
        public void Customer_NameExceeds255Chars_FailsValidation()
        {
            var customer = new Customer { Id = 1, Name = new string('Z', 256) };
            var results = ValidateModel(customer);

            Assert.IsTrue(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Name exceeding 255 characters should fail validation.");
        }

        /// <summary>
        /// Verifies that Name at exactly 255 characters passes validation.
        /// </summary>
        [TestMethod]
        public void Customer_NameAt255Chars_PassesValidation()
        {
            var customer = new Customer { Id = 1, Name = new string('B', 255) };
            var results = ValidateModel(customer);

            Assert.IsFalse(
                results.Any(r => r.MemberNames.Contains("Name")),
                "Name at exactly 255 characters should pass validation.");
        }

        /// <summary>
        /// Ensures default values are sensible.
        /// </summary>
        [TestMethod]
        public void Customer_Defaults()
        {
            var customer = new Customer();

            Assert.AreEqual(0, customer.Id);
            Assert.IsNull(customer.Name);
        }

        private static IList<ValidationResult> ValidateModel(object model)
        {
            var results = new List<ValidationResult>();
            var context = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, context, results, validateAllProperties: true);
            return results;
        }
    }
}
