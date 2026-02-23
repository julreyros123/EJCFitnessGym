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

    private static readonly string[] BackOfficeRequestPrefixes =
    {
        "/Admin",
        "/Finance",
        "/Staff",
        "/MemberAccounts",
        "/Invoices",
        "/SubscriptionPlans",
        "/Dashboard/SuperAdmin",
        "/AdminMembership",
        "/IntegrationOps",
        "/UserBranches"
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

    public static bool IsBackOfficeRequestPath(string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return false;
        }

        return BackOfficeRequestPrefixes.Any(prefix =>
            requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeBackOfficeReturnUrl(
        IUrlHelper url,
        string? returnUrl,
        IEnumerable<string>? roles)
    {
        var roleSet = (roles ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fallback = ResolveBackOfficeDefaultReturnUrl(roleSet);
        if (string.IsNullOrWhiteSpace(returnUrl) || !url.IsLocalUrl(returnUrl))
        {
            return fallback;
        }

        if (returnUrl.Contains("/Identity/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return CanAccessBackOfficePath(returnUrl, roleSet)
            ? returnUrl
            : fallback;
    }

    private static string ResolveBackOfficeDefaultReturnUrl(IReadOnlySet<string> roles)
    {
        if (roles.Contains("SuperAdmin"))
        {
            return "/Dashboard/SuperAdmin";
        }

        if (roles.Contains("Admin"))
        {
            return "/Admin/Dashboard";
        }

        if (roles.Contains("Finance"))
        {
            return "/Finance/Dashboard";
        }

        if (roles.Contains("Staff"))
        {
            return "/Staff/CheckIn";
        }

        return "/Dashboard/Index";
    }

    private static bool CanAccessBackOfficePath(string returnUrl, IReadOnlySet<string> roles)
    {
        if (roles.Count == 0)
        {
            return false;
        }

        if (returnUrl.StartsWith("/Dashboard/SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("SuperAdmin");
        }

        if (returnUrl.StartsWith("/Finance", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("Finance") && !roles.Contains("SuperAdmin");
        }

        if (returnUrl.StartsWith("/Staff", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("Staff") || roles.Contains("Admin") || roles.Contains("SuperAdmin");
        }

        if (returnUrl.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith("/MemberAccounts", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith("/AdminMembership", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith("/SubscriptionPlans", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith("/IntegrationOps", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("Admin") || roles.Contains("Finance") || roles.Contains("SuperAdmin");
        }

        if (returnUrl.StartsWith("/UserBranches", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("SuperAdmin");
        }

        if (returnUrl.StartsWith("/Invoices", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("Staff") || roles.Contains("Admin") || roles.Contains("Finance") || roles.Contains("SuperAdmin");
        }

        if (string.Equals(returnUrl, "/Dashboard", StringComparison.OrdinalIgnoreCase) ||
            returnUrl.StartsWith("/Dashboard/Index", StringComparison.OrdinalIgnoreCase))
        {
            return roles.Contains("Staff") || roles.Contains("Admin") || roles.Contains("Finance") || roles.Contains("SuperAdmin");
        }

        return false;
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
