using System.Reflection;

namespace Hardmob.Helpers
{
    /// <summary>
    /// Helper methods for <see cref="Assembly"/>
    /// </summary>
    static class AssemblyHelper
    {
        /// <summary>
        /// Get the assembly's product name
        /// </summary>
        public static string GetProductName(this Assembly assembly)
        {
            // Validate the input
            if (assembly != null)
            {
                // Get all product relative attribute
                foreach (object attribute in assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false))
                    if (attribute is AssemblyProductAttribute product && string.IsNullOrWhiteSpace(product.Product))
                        return product.Product;
            }

            // None found
            return null;
        }
    }
}
