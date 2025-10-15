window.localStorageHelper = {
    saveWorkOrderData: function (data) {
        try {
            const payload = data ?? {};
            localStorage.setItem("workOrderData", JSON.stringify(payload));
        } catch (err) {
            console.warn("[localStorageHelper] saveWorkOrderData failed", err);
        }
    },
    loadWorkOrderData: function () {
        try {
            return localStorage.getItem("workOrderData");
        } catch (err) {
            console.warn("[localStorageHelper] loadWorkOrderData failed", err);
            return null;
        }
    },
    clearWorkOrderData: function () {
        try {
            localStorage.removeItem("workOrderData");
        } catch (err) {
            console.warn("[localStorageHelper] clearWorkOrderData failed", err);
        }
    }
};
