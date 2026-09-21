/* Modulus UI shared Alpine components (CSP-safe).

   The Alpine CSP build cannot evaluate inline expressions such as
   x-data="{ show: true }" or x-on:click="show = !show" (it has no eval), so
   framework views reference the named components below instead, using only
   property / method references in their directives:

     x-data="mDismissibleAlert"   x-show="show"        x-on:click="dismiss"
     x-data="mCopyButton"         x-show="notCopied"   x-on:click="copy"
     x-data="mPasswordToggle"     x-bind:type="inputType"
                                  x-text="label"       x-on:click="toggle"
     x-data="mAutoSubmit"         x-on:change="submit"   (on a <form>)
     x-data="mResetOnSuccess"     x-on:htmx:after-request="reset"   (on an hx-post <form>)
     x-data="mLineItems"          x-on:click="add" / "remove"      (m-line-items rows)

   Themes must load this file BEFORE Alpine (Alpine dispatches alpine:init
   once, when it starts). It has no dependency on any theme, so feature UIs
   (Identity, Files, ...) work under any ITheme. Apps register their own
   components the same way, from a script that also loads before Alpine. */
document.addEventListener('alpine:init', function () {
    'use strict';

    function toastDuration() {
        var config = window.Modulus && window.Modulus.config;
        return (config && config.toastDuration) || 6000;
    }

    // Auto-dismissing alert (partial _Alert).
    Alpine.data('mDismissibleAlert', function () {
        return {
            show: true,
            timer: null,
            init: function () {
                var self = this;
                this.timer = setTimeout(function () { self.show = false; }, toastDuration());
            },
            destroy: function () { clearTimeout(this.timer); },
            dismiss: function () { this.show = false; }
        };
    });

    // "Copy" button: copies the clicked button's data-path, then flips its label for 1.5s.
    Alpine.data('mCopyButton', function () {
        return {
            copied: false,
            timer: null,
            get notCopied() { return !this.copied; },
            copy: function (event) {
                var self = this;
                var button = event && event.currentTarget;
                var text = button && button.dataset ? button.dataset.path : null;
                if (!text || !navigator.clipboard) return;
                navigator.clipboard.writeText(text).then(function () {
                    self.copied = true;
                    clearTimeout(self.timer);
                    self.timer = setTimeout(function () { self.copied = false; }, 1500);
                });
            },
            destroy: function () { clearTimeout(this.timer); }
        };
    });

    // Show/hide toggle for a password input. Labels come from data-show-label /
    // data-hide-label on the x-data element so views can localize them.
    Alpine.data('mPasswordToggle', function () {
        return {
            visible: false,
            get inputType() { return this.visible ? 'text' : 'password'; },
            get label() {
                var data = this.$el.dataset;
                return this.visible ? (data.hideLabel || 'Hide') : (data.showLabel || 'Show');
            },
            toggle: function () { this.visible = !this.visible; }
        };
    });

    // Submits the x-data <form> when one of its controls changes (filter switches). Replaces an
    // inline onchange="this.form.submit()", which a strict script-src policy blocks.
    // Clears the x-data <form> once its htmx request succeeded (create forms). A failed or 422
    // response keeps what the user typed. Replaces hx-on::after-request="this.reset()", inline script.
    Alpine.data('mResetOnSuccess', function () {
        return {
            reset: function (event) {
                var detail = event && event.detail;
                if (detail && detail.successful === false) return;
                var form = this.$el;
                if (form && typeof form.reset === 'function') form.reset();
            }
        };
    });

    Alpine.data('mAutoSubmit', function () {
        return {
            submit: function () {
                var form = this.$el;
                if (form && typeof form.requestSubmit === 'function') form.requestSubmit();
                else if (form && typeof form.submit === 'function') form.submit();
            }
        };
    });

    // Editable child rows (<m-line-items>). The server renders each row with its index in the field path
    // (Input.Lines[2].Sku) and one blank row inside <template data-line-template> whose index is the literal
    // __index__. "Add" clones that template with the next index; "remove" drops the row and renumbers the
    // rest, so the posted indexes stay 0..n-1 and the default collection binder (and the model-state keys of a
    // 422 re-render) line up. data-name-prefix / data-id-prefix on the x-data element name the collection
    // (Input.Lines / Input_Lines); nested collections work because only the first [n] after the prefix changes.
    Alpine.data('mLineItems', function () {
        function escapeRegExp(text) { return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

        function rowsOf(root) {
            var list = root.querySelector('[data-line-rows]');
            return Array.prototype.filter.call(list ? list.children : [], function (row) {
                return row.hasAttribute('data-line-row');
            });
        }

        function renumber(root) {
            var name = new RegExp('^' + escapeRegExp(root.dataset.namePrefix) + '\\[\\d+\\]');
            var id = new RegExp('^' + escapeRegExp(root.dataset.idPrefix) + '_\\d+__');
            rowsOf(root).forEach(function (row, index) {
                Array.prototype.forEach.call(row.querySelectorAll('[name],[id],[for]'), function (el) {
                    if (el.hasAttribute('name')) {
                        el.setAttribute('name', el.getAttribute('name').replace(name, root.dataset.namePrefix + '[' + index + ']'));
                    }
                    ['id', 'for'].forEach(function (attribute) {
                        if (el.hasAttribute(attribute)) {
                            el.setAttribute(attribute, el.getAttribute(attribute).replace(id, root.dataset.idPrefix + '_' + index + '__'));
                        }
                    });
                });
            });
        }

        return {
            add: function (event) {
                var root = event.currentTarget.closest('[data-line-items]');
                var template = root.querySelector('template[data-line-template]');
                var list = root.querySelector('[data-line-rows]');
                if (!template || !list) return;
                var holder = document.createElement('div');
                holder.innerHTML = template.innerHTML.split('__index__').join(String(rowsOf(root).length));
                while (holder.firstChild) list.appendChild(holder.firstChild);
            },
            remove: function (event) {
                var row = event.currentTarget.closest('[data-line-row]');
                if (!row) return;
                var root = row.closest('[data-line-items]');
                row.remove();
                renumber(root);
            }
        };
    });
});
