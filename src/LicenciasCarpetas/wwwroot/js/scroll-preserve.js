// Preserva la posición exacta de scroll (ventana y tabla horizontal/vertical) entre acciones,
// recargas, envíos de formularios y redirecciones, evitando saltos de pantalla molestos.
(function () {
    var key = 'scroll_state:' + location.pathname;

    function tableCard() {
        return document.querySelector('.card.table-card') || document.querySelector('.table-card');
    }

    function save() {
        var card = tableCard();
        var state = {
            windowY: window.scrollY || window.pageYOffset || 0,
            windowX: window.scrollX || window.pageXOffset || 0,
            cardX: card ? card.scrollLeft : 0,
            cardY: card ? card.scrollTop : 0,
            time: Date.now()
        };
        try {
            sessionStorage.setItem(key, JSON.stringify(state));
        } catch (e) {}
    }

    function restore() {
        var raw;
        try {
            raw = sessionStorage.getItem(key);
        } catch (e) {
            return;
        }
        if (!raw) return;

        var state;
        try {
            state = JSON.parse(raw);
        } catch (e) {
            return;
        }

        if (!state) return;

        // Restaurar ventana
        if (typeof state.windowY === 'number' || typeof state.windowX === 'number') {
            window.scrollTo(state.windowX || 0, state.windowY || 0);
        }

        // Restaurar contenedor de tabla interna
        var card = tableCard();
        if (card) {
            if (typeof state.cardX === 'number') card.scrollLeft = state.cardX;
            if (typeof state.cardY === 'number') card.scrollTop = state.cardY;
        }
    }

    if ('scrollRestoration' in history) {
        try {
            history.scrollRestoration = 'manual';
        } catch (e) {}
    }

    // Guardar posición al enviar cualquier formulario o salir de la página
    document.addEventListener('submit', function (event) {
        save();
    }, true);

    window.addEventListener('beforeunload', save);

    // Guardar también continuamente con debounce en scroll
    var scrollSaveTimeout = null;
    function debounceSave() {
        if (scrollSaveTimeout) clearTimeout(scrollSaveTimeout);
        scrollSaveTimeout = setTimeout(save, 100);
    }

    window.addEventListener('scroll', debounceSave, { passive: true });

    function attachCardListener() {
        var card = tableCard();
        if (card) {
            card.addEventListener('scroll', debounceSave, { passive: true });
        }
    }

    function runRestoreSequence() {
        restore();
        requestAnimationFrame(restore);
        setTimeout(restore, 50);
        setTimeout(restore, 150);
        setTimeout(restore, 350);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            attachCardListener();
            runRestoreSequence();
        });
    } else {
        attachCardListener();
        runRestoreSequence();
    }

    window.addEventListener('load', runRestoreSequence);
})();
