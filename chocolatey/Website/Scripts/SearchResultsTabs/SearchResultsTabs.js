function SearchResultsTabs() {
    var $docsTab = $(".search-results li.docs-tab");
    var $docsView = $(".search-results .docs-results");
    var $packagesTab = $(".search-results li.packages-tab");
    var $packagesView = $(".search-results .packages-results");

    $docsTab.click(function (e) {
        $docsTab.addClass("selected");
        $docsView.show();
        $packagesTab.removeClass("selected");
        $packagesView.hide();
    });

    $packagesTab.click(function (e) {
        $docsTab.removeClass("selected");
        $docsView.hide();
        $packagesTab.addClass("selected");
        $packagesView.show();
    });
}