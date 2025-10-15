// scripts.js
function showPass() {
    const b = document.getElementById('resultBadge');
    const pass = Math.random() > .35;
    b.className = 'badge ' + (pass ? 'pass' : 'fail');
    b.textContent = pass ? '✅ PASS' : '❌ FAIL';
}