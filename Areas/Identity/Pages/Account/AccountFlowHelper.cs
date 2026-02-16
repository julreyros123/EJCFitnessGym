using Microsoft.AspNetCore.Mvc;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

internal static class AccountFlowHelper
{
    private static readonly string[] RestrictedMemberReturnPrefixes =
    {
        "/Admin",
        "/Finance",
        "/Staff",
        "/Invoices",
        "/SubscriptionPlans"
    };

    private static readonly string[] BackOfficeRoles =
    {
        "Staff",
        "Finance",
        "Admin",
        "SuperAdmin"
    };

    public static string NormalizeMemberReturnUrl(IUrlHelper url, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !url.IsLocalUrl(returnUrl))
        {
            return url.Content("~/");
        }

        if (RestrictedMemberReturnPrefixes.Any(prefix => returnUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            returnUrl.Contains("/Identity/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            return "/Dashboard/Index";
        }

        return returnUrl;
    }

    public static bool IsBackOfficeRole(string role)
    {
        return BackOfficeRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    public static string? BuildAbsolutePageUrl(
        IUrlHelper url,
        HttpRequest request,
        IConfiguration configuration,
        string page,
        object routeValues)
    {
        var callbackPath = url.Page(page, pageHandler: null, values: routeValues);
        if (string.IsNullOrWhiteSpace(callbackPath))
        {
            return null;
        }

        var publicBaseUrl = configuration["App:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(publicBaseUrl) && Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, callbackPath).ToString();
        }

        return url.Page(
            page,
            pageHandler: null,
            values: routeValues,
            protocol: request.Scheme);
    }
}
