namespace ETABSModelDefinitionValidator.Licensing
{
    /// <summary>License identity shared by the desktop UI and the console runner.
    /// <see cref="ProductId"/> must never change once a license names it.</summary>
    internal static class LicenseInfo
    {
        public const string ProductId = "ETABSModelValidator";
        public const string Name = "ETABS Model Definition Validator";
        public const string IssuerEmail = "mostafa.mohamed0101000@gmail.com";

        /// <summary>Raw URL of the signed revocation list (LicenseMaker's Block tab writes it). Empty = online
        /// checking off, licenses end only by expiry. Must be set before shipping a build that should be blockable.</summary>
        public const string RevocationUrl = "https://gist.githubusercontent.com/mostafamohamed-d/16b6f9910e5898cfafd47f95909a429f/raw/revoked.lrev";

        /// <summary>Shows the activation dialog when this PC has no valid license; false = not licensed.</summary>
        public static bool EnsureLicensed()
        {
            ToolLicensing.LicensingOptions.ContactEmail = IssuerEmail;
            ToolLicensing.LicensingOptions.ContactText = "Click Email request, attach the file to the email and send it to " + IssuerEmail + ".";
            ToolLicensing.LicensingOptions.RevocationUrl = RevocationUrl;
            return ToolLicensing.LicenseGate.EnsureLicensed(ProductId, Name);
        }
    }
}
