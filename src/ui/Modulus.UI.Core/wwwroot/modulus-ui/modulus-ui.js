/* Modulus UI shared client behavior (vanilla JS — no htmx/Alpine dependency
   for the critical path, so CSRF protection and error feedback work even if a
   vendored bundle fails to load).
   - htmx:configRequest: attach the ASP.NET antiforgery request token (rendered
     once by the shared layout via @Html.AntiForgeryToken()) as the
     RequestVerificationToken header on every htmx request.
   - htmx:afterRequest: render server-sent toast triggers
     (HX-Trigger: {"modulusToast": {"message": "...", "type": "success"}} —
     the "modulus:toast" alias is also accepted) as dismissible Tabler alerts
     into #modulus-toasts.
   - htmx:confirm: replace the native browser confirm dialog used by
     hx-confirm with the shared Tabler #modulus-confirm modal.
   - htmx:responseError / htmx:sendError: surface a danger toast so failed
     swaps are never silent.
   HTMX edge cases handled here (see HtmxPageModel / HtmxResponse):
   - 422 re-renders the form partial with validation errors (htmx 2 does not
     swap 4xx by default, so responseHandling maps 422 to swap:true).
   - Auth redirects use HX-Redirect (sent by UseModulusHtmxRedirects), which
     htmx follows natively — no client code needed.
   - Navigation uses hx-boost on <body> (see _UiLayout): normal links become
     boosted htmx navigations; fragment swaps keep Layout = null via
     IsHtmxFragment, boosted ones render the full shell.
   - CSP: no eval, no inline-script execution from swaps
     (allowEval:false, allowScriptTags handled by htmx default), no
     indicator-style injection (includeIndicatorStyles:false — Tabler/CSS
     owns the spinner). Pair with the Alpine CSP build and Alpine.data()
     registrations instead of inline expressions. */
(function () {
    if (typeof htmx !== 'undefined') {
        htmx.config.responseHandling = [
            { code: '204', swap: false },
            { code: '[23]..', swap: true },
            { code: '422', swap: true },
            { code: '[45]..', swap: false, error: true }
        ];
        htmx.config.allowEval = false;
        htmx.config.includeIndicatorStyles = false;
    }

    function antiforgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    function showModulusToast(message, type) {
        var host = document.getElementById('modulus-toasts');
        if (!host) return;
        var color = type || 'success';
        var el = document.createElement('div');
        el.className = 'alert alert-' + color + ' alert-dismissible mb-2';
        el.setAttribute('role', 'alert');
        var text = document.createElement('div');
        text.textContent = message;
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close';
        close.setAttribute('aria-label', 'Close');
        close.addEventListener('click', function () { el.remove(); });
        el.appendChild(text);
        el.appendChild(close);
        host.appendChild(el);
        setTimeout(function () { el.remove(); }, 6000);
    }

    function toastFromPayload(payload) {
        if (!payload) return null;
        if (payload.modulusToast && payload.modulusToast.message) return payload.modulusToast;
        if (payload['modulus:toast'] && payload['modulus:toast'].message) return payload['modulus:toast'];
        return null;
    }

    document.body.addEventListener('htmx:configRequest', function (event) {
        var token = antiforgeryToken();
        if (token) event.detail.headers['RequestVerificationToken'] = token;
    });

    // Replace the native hx-confirm dialog with the shared Tabler modal.
    // htmx fires htmx:confirm with { question, issueRequest }; calling
    // preventDefault() takes over, and issueRequest() continues on confirm.
    var pendingConfirm = null;

    function hideConfirmModal() {
        var modal = document.getElementById('modulus-confirm');
        if (!modal) return;
        if (window.bootstrap && window.bootstrap.Modal) {
            var instance = window.bootstrap.Modal.getInstance(modal);
            if (instance) instance.hide();
            return;
        }
        modal.classList.remove('show');
        modal.style.display = 'none';
        modal.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('modal-open');
        var backdrop = document.querySelector('.modal-backdrop');
        if (backdrop) backdrop.remove();
    }

    function showConfirmModal(message, onConfirm) {
        var modal = document.getElementById('modulus-confirm');
        // No shared modal on this page (or unit-test DOM): native fallback.
        if (!modal) {
            if (window.confirm(message)) onConfirm();
            return;
        }
        var body = document.getElementById('modulus-confirm-body');
        if (body) body.textContent = message;
        pendingConfirm = onConfirm;
        if (window.bootstrap && window.bootstrap.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(modal).show();
            return;
        }
        modal.classList.add('show');
        modal.style.display = 'block';
        modal.removeAttribute('aria-hidden');
        document.body.classList.add('modal-open');
    }

    document.body.addEventListener('htmx:confirm', function (event) {
        if (!event.detail || !event.detail.question) return;
        event.preventDefault();
        showConfirmModal(event.detail.question, function () {
            event.detail.issueRequest();
        });
    });

    document.body.addEventListener('click', function (event) {
        if (event.target && event.target.id === 'modulus-confirm-ok') {
            var next = pendingConfirm;
            pendingConfirm = null;
            hideConfirmModal();
            if (next) next();
        }
        if (event.target && event.target.hasAttribute &&
            event.target.hasAttribute('data-bs-dismiss') &&
            event.target.closest('#modulus-confirm')) {
            pendingConfirm = null;
            hideConfirmModal();
        }
    });

    document.body.addEventListener('htmx:afterRequest', function (event) {
        var raw = event.detail.xhr.getResponseHeader('HX-Trigger');
        if (!raw) return;
        var payload;
        try { payload = JSON.parse(raw); }
        catch (e) {
            // Bare trigger name (e.g. "products:changed") — nothing to render.
            return;
        }
        var toast = toastFromPayload(payload);
        if (toast) showModulusToast(toast.message, toast.type);
    });

    function failureToast() {
        showModulusToast('Request failed. Please try again.', 'danger');
    }

    document.body.addEventListener('htmx:responseError', failureToast);
    document.body.addEventListener('htmx:sendError', failureToast);

    window.modulusUi = { showToast: showModulusToast };
})();
