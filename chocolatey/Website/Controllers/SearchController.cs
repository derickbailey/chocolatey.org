using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.UI;
using NuGet;
using NugetGallery;

namespace NuGetGallery
{
    public partial class SearchController : Controller
    {

        private readonly ISearchService searchSvc;
        private readonly IPackageService packageSvc;

        public SearchController(IPackageService packageSvc, ISearchService searchService)
        {
            this.packageSvc = packageSvc;
            this.searchSvc = searchService;
        }

        [HttpGet, OutputCache(VaryByParam = "*", Location = OutputCacheLocation.Any, Duration = 30)]
        public virtual ActionResult DoSearch(string q, string sortOrder = null, int page = 1, bool prerelease = false, bool moderatorQueue = false)
        {
            if (page < 1) page = 1;
            q = (q ?? string.Empty).Trim();

            IQueryable<Package> packageVersions = packageSvc.GetPackagesForListing(prerelease);
            IEnumerable<Package> packagesToShow = new List<Package>();

            if (moderatorQueue)
            {
                var unknownStatus = PackageStatusType.Unknown.GetDescriptionOrValue();

                //This is going to be fun. Unknown status ones would be listed, but sometimes a few might slip through the cracks if a maintainer unlists a package.
                // A user can just email us to catch those though.
                packageVersions = packageVersions.Where(p => !p.IsPrerelease).Where(p => p.StatusForDatabase == unknownStatus || p.StatusForDatabase == null);
            }

            q = (q ?? "").Trim();

            if (String.IsNullOrEmpty(sortOrder))
            {
                // Determine the default sort order. If no query string is specified, then the sortOrder is DownloadCount
                // If we are searching for something, sort by relevance.
                sortOrder = q.IsEmpty() ? Constants.PopularitySortOrder : Constants.RelevanceSortOrder;
            }

            int totalHits = 0;
            int updatedPackagesCount = 0;
            int respondedPackagesCount = 0;
            int unreviewedPackagesCount = 0;
            int waitingPackagesCount = 0;
            var searchFilter = GetSearchFilter(q, sortOrder, page, prerelease);

            if (moderatorQueue)
            {
                var submittedPackages = packageSvc.GetSubmittedPackages(useCache: !Request.IsAuthenticated).ToList();

                var updatedStatus = PackageSubmittedStatusType.Updated.ToString();
                var respondedStatus = PackageSubmittedStatusType.Responded.ToString();
                var readyStatus = PackageSubmittedStatusType.Ready.ToString();
                var pendingStatus = PackageSubmittedStatusType.Pending.ToString();
                var waitingStatus = PackageSubmittedStatusType.Waiting.ToString();

                //var resubmittedPackages = submittedPackages.Where(p => p.ReviewedDate.HasValue && p.Published > p.ReviewedDate).OrderBy(p => p.Published).ToList();
                var resubmittedPackages = submittedPackages.Where(p => p.SubmittedStatusForDatabase == updatedStatus).OrderBy(p => p.Published).ToList();
                updatedPackagesCount = resubmittedPackages.Count;

                var respondedPackages = submittedPackages.Where(p => p.SubmittedStatusForDatabase == respondedStatus).OrderBy(p => p.LastUpdated).ToList();
                respondedPackagesCount = respondedPackages.Count;

                var unreviewedPackages = submittedPackages.Where(p => p.SubmittedStatusForDatabase == readyStatus).OrderBy(p => p.Published).ToList();
                unreviewedPackagesCount = unreviewedPackages.Count;

                var pendingAutoReviewPackages = submittedPackages.Where(p => p.SubmittedStatusForDatabase == pendingStatus || p.SubmittedStatusForDatabase == null).OrderBy(p => p.Published).ToList();
                unreviewedPackagesCount += pendingAutoReviewPackages.Count;

                //var waitingForMaintainerPackages = submittedPackages.Where(p => p.ReviewedDate >= p.Published).OrderByDescending(p => p.ReviewedDate).ToList();
                var waitingForMaintainerPackages = submittedPackages.Where(p => p.SubmittedStatusForDatabase == waitingStatus).OrderByDescending(p => p.ReviewedDate).ToList();
                waitingPackagesCount = waitingForMaintainerPackages.Count;

                packagesToShow = resubmittedPackages.Union(respondedPackages).Union(unreviewedPackages).Union(pendingAutoReviewPackages).Union(waitingForMaintainerPackages);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    packagesToShow = packagesToShow.AsQueryable().Search(q).ToList();
                }

                switch (searchFilter.SortProperty)
                {
                    case SortProperty.DisplayName:
                        packagesToShow = packagesToShow.OrderBy(p => p.Title);
                        break;
                    case SortProperty.Recent:
                        packagesToShow = packagesToShow.OrderByDescending(p => p.Published);
                        break;
                    default:
                        //do not change the search order
                        break;
                }

                totalHits = packagesToShow.Count() + packageVersions.Count();

                if ((searchFilter.Skip + searchFilter.Take) >= packagesToShow.Count() & string.IsNullOrWhiteSpace(q)) packagesToShow = packagesToShow.Union(packageVersions.OrderByDescending(pv => pv.PackageRegistration.DownloadCount).ToList());

                packagesToShow = packagesToShow.Skip(searchFilter.Skip).Take(searchFilter.Take);
            }
            else
            {
                var results = searchSvc.Search(searchFilter);

                var cacheTime = DateTime.UtcNow.AddSeconds(30);
                // fetch most common query from cache to relieve load on the search service
                if (string.IsNullOrEmpty(q) && page == 1)
                {
                    cacheTime = DateTime.UtcNow.AddMinutes(10);
                }

                totalHits = 0;
                int.TryParse(Cache.Get(
                   string.Format(
                       "searchResultsHits-{0}-{1}-{2}-{3}-{4}",
                       searchFilter.SearchTerm.to_lower(),
                       searchFilter.IncludePrerelease,
                       searchFilter.Skip,
                       searchFilter.SortProperty.to_string(),
                       searchFilter.SortDirection.to_string()),
                   cacheTime,
                   () => results.Hits.to_string()), out totalHits);

                packagesToShow = Cache.Get(
                   string.Format(
                       "searchResults-{0}-{1}-{2}-{3}-{4}",
                       searchFilter.SearchTerm.to_lower(),
                       searchFilter.IncludePrerelease,
                       searchFilter.Skip,
                       searchFilter.SortProperty.to_string(),
                       searchFilter.SortDirection.to_string()),
                   cacheTime,
                   () => results.Data.ToList());
            }

            if (page == 1 && !packagesToShow.Any())
            {
                // In the event the index wasn't updated, we may get an incorrect count. 
                totalHits = 0;
            }

            var viewModel = new PackageListViewModel(
                packagesToShow, q, sortOrder, totalHits, page - 1, Constants.DefaultPackageListPageSize, Url, prerelease, moderatorQueue, updatedPackagesCount, unreviewedPackagesCount, waitingPackagesCount, respondedPackagesCount);

            ViewBag.SearchTerm = q;

            return View("~/Views/Search/SearchResults.cshtml", viewModel);
        }

        private SearchFilter GetSearchFilter(string q, string sortOrder, int page, bool includePrerelease)
        {
            var searchFilter = new SearchFilter
            {
                SearchTerm = q,
                Skip = (page - 1) * Constants.DefaultPackageListPageSize, // pages are 1-based. 
                Take = Constants.DefaultPackageListPageSize,
                IncludePrerelease = includePrerelease
            };

            switch (sortOrder)
            {
                case Constants.AlphabeticSortOrder:
                    searchFilter.SortProperty = SortProperty.DisplayName;
                    searchFilter.SortDirection = SortDirection.Ascending;
                    break;
                case Constants.RecentSortOrder:
                    searchFilter.SortProperty = SortProperty.Recent;
                    break;
                case Constants.PopularitySortOrder:
                    searchFilter.SortProperty = SortProperty.DownloadCount;
                    break;
                default:
                    searchFilter.SortProperty = SortProperty.Relevance;
                    break;
            }
            return searchFilter;
        }
    }
}