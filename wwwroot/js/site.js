// Small scripts used on many pages.

// 1) Consultation timer.
//    <div class="js-timer" data-start="(milliseconds)" data-limit="10">00:00</div>
//    Counts up every second and turns red after the limit (10 minutes).
(function () {
    var timers = document.querySelectorAll('.js-timer');
    if (timers.length === 0) return;

    function tick() {
        timers.forEach(function (el) {
            var seconds = Math.max(0, Math.floor((Date.now() - Number(el.dataset.start)) / 1000));
            var mm = String(Math.floor(seconds / 60)).padStart(2, '0');
            var ss = String(seconds % 60).padStart(2, '0');
            el.textContent = mm + ':' + ss;
            if (seconds > Number(el.dataset.limit) * 60) el.classList.add('overtime');
        });
    }
    tick();
    setInterval(tick, 1000);
})();

// 2) Auto refresh for live pages (queue, my serial).
//    Put  <div data-auto-refresh="20"></div>  on the page to reload every 20 seconds.
//    It waits if you are typing in a box, so you don't lose what you typed.
(function () {
    var marker = document.querySelector('[data-auto-refresh]');
    if (!marker) return;

    var seconds = Number(marker.dataset.autoRefresh) || 20;

    setInterval(function () {
        var el = document.activeElement;
        var typing = el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT');
        if (!typing) location.reload();
    }, seconds * 1000);
})();
