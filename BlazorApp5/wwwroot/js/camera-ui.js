// wwwroot/js/camera-ui.js
window.cameraUi = (function () {
    let dotnetRef = null;
    function onMove(e) {
        if (!dotnetRef) return;
        // ส่งค่าไปหา C# ทุก ~80ms กันสแปม
        if (onMove._t) return;
        onMove._t = setTimeout(() => {
            onMove._t = null;
        }, 80);
        dotnetRef.invokeMethodAsync('OnMouseChanged', e.clientX, e.clientY);
    }
    return {
        initMouse: function (ref) {
            dotnetRef = ref;
            window.addEventListener('mousemove', onMove);
        }
    };
})();
