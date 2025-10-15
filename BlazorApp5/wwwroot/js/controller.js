// Placeholder controller script to avoid 404s when the page requests the legacy controller bundle.
// If future functionality requires JS interop hooks, implement them here.
window.appController = window.appController || {
    log: function (message) {
        if (window.console && typeof window.console.log === "function") {
            console.log("[controller.js]", message);
        }
    }
};
