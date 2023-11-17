using System.ComponentModel;
using System.Configuration.Install;

namespace Hardmob
{
    /// <summary>
    /// Service installer
    /// </summary>
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        /// <summary>
        /// New installer
        /// </summary>
        public ProjectInstaller()
        {
            // Initialize components
            this.InitializeComponent();
        }
    }
}
