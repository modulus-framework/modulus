/* Modulus UI client runtime (Tabler theme).

   Vanilla JS, no build step, CSP-safe (no eval, no inline handlers). Exposes a
   single global, `Modulus`:

     Modulus.config          tweakable settings (toastDuration, confirm, ...)
     Modulus.onLoad(cb)      cb(root) runs on page load and after every htmx
                             swap-in (root = the new content)
     Modulus.components      register(name, { mount(el), unmount(el) }) for
                             elements marked data-m-component="name"
     Modulus.toast(type, msg)
     Modulus.confirm(msg)    -> Promise<boolean> (uses Modulus.config.confirm)
     Modulus.colorMode       get() / set('light'|'dark'|'system')
     Modulus.getAntiforgeryToken()

   Server contract (see HtmxResponse):
     HX-Trigger: modulusToast | modulus:toast   {message, type}  -> toast
     HX-Trigger: modulus:modal:close                             -> close modal
     hx-get/hx-post targeting #m-modal-container                 -> modal opens
     422 responses swap normally (server re-renders the form with errors);
     other 4xx/5xx raise a danger toast.

   This file must be loaded after htmx and before Alpine (the Tabler layouts
   already order the scripts that way). */
(function () {
    'use strict';

    var config = {
        csrfHeader: 'RequestVerificationToken',
        toastDuration: 6000,
        failureMessage: 'Request failed. Please try again.',
        timezone: (window.Intl && Intl.DateTimeFormat().resolvedOptions().timeZone) || null,
        locale: document.documentElement.lang || 'en',
        confirm: null // assigned below; replace with (message) => Promise<boolean>
    };

    // ---- htmx configuration (CSP: no eval, no injected indicator styles) ----
    if (typeof htmx !== 'undefined') {
        htmx.config.responseHandling = [
            { code: '204', swap: false },
            { code: '[23]..', swap: true },
            { code: '422', swap: true },
            { code: '[45]..', swap: false, error: true }
        ];
        htmx.config.allowEval = false;
        htmx.config.includeIndicatorStyles = false;
        htmx.config.historyCacheSize = 20;
    }

    // ---- antiforgery ----
    function getAntiforgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    document.addEventListener('htmx:configRequest', function (event) {
        var token = getAntiforgeryToken();
        if (token) event.detail.headers[config.csrfHeader] = token;
    });

    // ---- toasts ----
    function toast(type, message) {
        var host = document.getElementById('modulus-toasts');
        if (!host || !message) return;
        var el = document.createElement('div');
        el.className = 'alert alert-' + (type || 'success') + ' alert-dismissible mb-2';
        el.setAttribute('role', 'alert');
        var text = document.createElement('div');
        text.textContent = message; // textContent: server text is never parsed as HTML
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close';
        close.setAttribute('aria-label', 'Close');
        close.addEventListener('click', function () { el.remove(); });
        el.appendChild(text);
        el.appendChild(close);
        host.appendChild(el);
        setTimeout(function () { el.remove(); }, config.toastDuration);
    }

    function onToastEvent(event) {
        var d = event.detail || {};
        toast(d.type, d.message);
    }
    document.addEventListener('modulusToast', onToastEvent);
    document.addEventListener('modulus:toast', onToastEvent);

    function failureToast() { toast('danger', config.failureMessage); }
    document.addEventListener('htmx:responseError', failureToast);
    document.addEventListener('htmx:sendError', failureToast);

    // ---- modal host ----
    function modalEl() { return document.getElementById('m-modal'); }
    function bootstrapModal(el) {
        return window.bootstrap && window.bootstrap.Modal
            ? window.bootstrap.Modal.getOrCreateInstance(el)
            : null;
    }

    function openModal() {
        var el = modalEl();
        if (!el) return;
        var instance = bootstrapModal(el);
        if (instance) { instance.show(); return; }
        el.classList.add('show');
        el.style.display = 'block';
        el.removeAttribute('aria-hidden');
    }

    function closeModal() {
        var el = modalEl();
        if (!el) return;
        var instance = bootstrapModal(el);
        if (instance) { instance.hide(); return; }
        el.classList.remove('show');
        el.style.display = 'none';
        el.setAttribute('aria-hidden', 'true');
        clearModal();
    }

    function clearModal() {
        var container = document.getElementById('m-modal-container');
        if (container) container.innerHTML = '';
    }

    document.addEventListener('modulus:modal:close', closeModal);
    document.addEventListener('hidden.bs.modal', function (event) {
        if (event.target && event.target.id === 'm-modal') clearModal();
    });
    document.addEventListener('htmx:afterSwap', function (event) {
        var target = event.detail && event.detail.target;
        if (target && target.id === 'm-modal-container' && target.childElementCount > 0) openModal();
    });

    // ---- confirm (replaces window.confirm for hx-confirm) ----
    function defaultConfirm(message) {
        return new Promise(function (resolve) {
            var modal = document.getElementById('modulus-confirm');
            if (!modal) { resolve(window.confirm(message)); return; }

            var body = document.getElementById('modulus-confirm-body');
            var ok = document.getElementById('modulus-confirm-ok');
            var instance = bootstrapModal(modal);
            var answered = false;

            function finish(answer) {
                if (answered) return;
                answered = true;
                ok.removeEventListener('click', onOk);
                modal.removeEventListener('hidden.bs.modal', onHidden);
                resolve(answer);
            }
            function onOk() { finish(true); if (instance) instance.hide(); }
            function onHidden() { finish(false); }

            if (body) body.textContent = message;
            ok.addEventListener('click', onOk);
            modal.addEventListener('hidden.bs.modal', onHidden);
            if (instance) instance.show(); else finish(window.confirm(message));
        });
    }
    config.confirm = defaultConfirm;

    document.addEventListener('htmx:confirm', function (event) {
        if (!event.detail || !event.detail.question) return;
        event.preventDefault();
        Promise.resolve(config.confirm(event.detail.question)).then(function (ok) {
            if (ok) event.detail.issueRequest(true);
        });
    });

    // ---- color mode ----
    var COLOR_COOKIE = 'modulus-color-mode';
    var mq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function applyColorMode(mode) {
        var root = document.documentElement;
        var resolved = mode === 'system' ? (mq && mq.matches ? 'dark' : 'light') : mode;
        root.setAttribute('data-bs-theme', resolved);
        root.setAttribute('data-m-color-mode', mode);
    }

    var colorMode = {
        get: function () { return document.documentElement.getAttribute('data-m-color-mode') || 'light'; },
        set: function (mode) {
            if (mode !== 'light' && mode !== 'dark' && mode !== 'system') return;
            document.cookie = COLOR_COOKIE + '=' + mode + '; path=/; max-age=31536000; samesite=lax';
            applyColorMode(mode);
        }
    };

    if (mq && mq.addEventListener) {
        mq.addEventListener('change', function () {
            if (colorMode.get() === 'system') applyColorMode('system');
        });
    }
    // The server renders `light` for `system`; resolve it client-side right away.
    if (colorMode.get() === 'system') applyColorMode('system');

    document.addEventListener('click', function (event) {
        var toggle = event.target && event.target.closest && event.target.closest('[data-m-color-mode-toggle]');
        if (toggle) {
            var current = document.documentElement.getAttribute('data-bs-theme');
            colorMode.set(current === 'dark' ? 'light' : 'dark');
            return;
        }
        var setter = event.target && event.target.closest && event.target.closest('[data-m-color-mode-set]');
        if (setter) colorMode.set(setter.getAttribute('data-m-color-mode-set'));
    });

    // ---- lifecycle: onLoad + component registry ----
    var loadCallbacks = [];
    var registry = {};

    function mountComponents(root) {
        if (!root || !root.querySelectorAll) return;
        var nodes = root.querySelectorAll('[data-m-component]');
        if (root.matches && root.matches('[data-m-component]')) nodes = [root].concat(Array.prototype.slice.call(nodes));
        Array.prototype.forEach.call(nodes, function (el) {
            var name = el.getAttribute('data-m-component');
            var component = registry[name];
            if (!component || el.__mModulusMounted === name) return;
            el.__mModulusMounted = name;
            if (component.mount) component.mount(el);
        });
    }

    function runLoad(root) {
        mountComponents(root);
        loadCallbacks.forEach(function (cb) { cb(root); });
        document.dispatchEvent(new CustomEvent('modulus:loaded', { detail: { root: root } }));
    }

    document.addEventListener('htmx:beforeCleanupElement', function (event) {
        var el = event.target;
        var name = el && el.__mModulusMounted;
        if (name && registry[name] && registry[name].unmount) registry[name].unmount(el);
    });

    function onLoad(cb) {
        if (typeof cb !== 'function') return;
        loadCallbacks.push(cb);
        if (loaded) cb(document.body);
    }

    var components = {
        register: function (name, component) {
            registry[name] = component;
            if (loaded) mountComponents(document.body);
        }
    };

    // htmx fires htmx:load on the initial body (from its own DOMContentLoaded
    // handler, registered before ours) and on every swapped-in element. The
    // DOMContentLoaded fallback below only runs the initial pass when htmx did
    // not, so onLoad callbacks never fire twice for the first page load.
    var loaded = false;
    function startInitialLoad() {
        if (loaded) return;
        loaded = true;
        runLoad(document.body);
    }

    if (typeof htmx !== 'undefined') {
        document.addEventListener('htmx:load', function (event) {
            loaded = true;
            runLoad(event.detail && event.detail.elt ? event.detail.elt : document.body);
        });
    }

    if (document.readyState === 'loading' || (typeof htmx !== 'undefined' && document.readyState === 'interactive')) {
        document.addEventListener('DOMContentLoaded', startInitialLoad);
    } else {
        startInitialLoad();
    }

    window.Modulus = {
        config: config,
        onLoad: onLoad,
        components: components,
        toast: toast,
        confirm: function (message) { return Promise.resolve(config.confirm(message)); },
        colorMode: colorMode,
        getAntiforgeryToken: getAntiforgeryToken
    };
})();
