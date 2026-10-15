namespace Tauntastic.Packages.Editor
{
    /// <summary>Menu priorities that keep the Tauntastic Packages entries at the top of the Tools > Tauntastic menu (lower numbers come first, the default is 1000), and the name of this package so lists can put it first.</summary>
    public static class PackageMenuPriority
    {
        public const string SelfPackageName = "com.tauntastic.packages";

        public const int Updates = -1000;
    }
}
