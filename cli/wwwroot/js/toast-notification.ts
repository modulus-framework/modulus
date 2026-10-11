/**
 * ToastNotification - Display toast notifications
 * Supports different notification types (success, error, warning, info)
 */

export interface ToastOptions {
    duration?: number;
    position?: 'top-start' | 'top-center' | 'top-end' | 'bottom-start' | 'bottom-center' | 'bottom-end';
    dismissible?: boolean;
    animation?: boolean;
    containerClass?: string;
}

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export class ToastNotification {
    private container: HTMLElement | null = null;
    private options: ToastOptions;

    constructor(options: ToastOptions = {}) {
        this.options = {
            duration: 5000,
            position: 'top-end',
            dismissible: true,
            animation: true,
            containerClass: 'toast-container',
            ...options
        };

        this.initializeContainer();
    }

    /**
     * Initialize toast container
     */
    private initializeContainer(): void {
        let container = document.querySelector(`.${this.options.containerClass}`);

        if (!container) {
            container = document.createElement('div');
            container.className = this.options.containerClass!;
            container.style.position = 'fixed';
            container.style.zIndex = '9999';
            this.setPositionStyles(container as HTMLElement);
            document.body.appendChild(container);
        }

        this.container = container as HTMLElement;
    }

    /**
     * Set position styles
     */
    private setPositionStyles(element: HTMLElement): void {
        const position = this.options.position || 'top-end';
        element.style.display = 'flex';
        element.style.flexDirection = 'column';
        element.style.gap = '0.5rem';

        switch (position) {
            case 'top-start':
                element.style.top = '1rem';
                element.style.left = '1rem';
                break;
            case 'top-center':
                element.style.top = '1rem';
                element.style.left = '50%';
                element.style.transform = 'translateX(-50%)';
                break;
            case 'top-end':
                element.style.top = '1rem';
                element.style.right = '1rem';
                break;
            case 'bottom-start':
                element.style.bottom = '1rem';
                element.style.left = '1rem';
                break;
            case 'bottom-center':
                element.style.bottom = '1rem';
                element.style.left = '50%';
                element.style.transform = 'translateX(-50%)';
                break;
            case 'bottom-end':
                element.style.bottom = '1rem';
                element.style.right = '1rem';
                break;
        }
    }

    /**
     * Show toast notification
     */
    show(message: string, type: ToastType = 'info'): HTMLElement {
        if (!this.container) {
            this.initializeContainer();
        }

        const toast = this.createToastElement(message, type);
        this.container!.appendChild(toast);

        // Auto-dismiss
        if (this.options.duration && this.options.duration > 0) {
            setTimeout(() => {
                this.removeToast(toast);
            }, this.options.duration);
        }

        return toast;
    }

    /**
     * Create toast element
     */
    private createToastElement(message: string, type: ToastType): HTMLElement {
        const toast = document.createElement('div');
        toast.className = `alert alert-${this.getAlertClass(type)} alert-dismissible fade show`;
        toast.setAttribute('role', 'alert');

        // Animation class
        if (this.options.animation) {
            toast.classList.add('toast-animate');
        }

        // Toast content
        const content = document.createElement('div');
        content.className = 'toast-content';

        // Icon
        const icon = document.createElement('span');
        icon.className = `toast-icon bi bi-${this.getIconClass(type)} me-2`;
        content.appendChild(icon);

        // Message
        const messageSpan = document.createElement('span');
        messageSpan.className = 'toast-message';
        messageSpan.textContent = message;
        content.appendChild(messageSpan);

        toast.appendChild(content);

        // Dismiss button
        if (this.options.dismissible) {
            const closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.className = 'btn-close';
            closeBtn.setAttribute('aria-label', 'Close');
            closeBtn.addEventListener('click', () => this.removeToast(toast));
            toast.appendChild(closeBtn);
        }

        return toast;
    }

    /**
     * Get alert class for type
     */
    private getAlertClass(type: ToastType): string {
        switch (type) {
            case 'success':
                return 'success';
            case 'error':
                return 'danger';
            case 'warning':
                return 'warning';
            case 'info':
            default:
                return 'info';
        }
    }

    /**
     * Get icon class for type
     */
    private getIconClass(type: ToastType): string {
        switch (type) {
            case 'success':
                return 'check-circle';
            case 'error':
                return 'exclamation-circle';
            case 'warning':
                return 'exclamation-triangle';
            case 'info':
            default:
                return 'info-circle';
        }
    }

    /**
     * Remove toast
     */
    private removeToast(toast: HTMLElement): void {
        toast.classList.remove('show');
        toast.classList.add('hide');

        setTimeout(() => {
            toast.remove();
        }, 300);
    }

    /**
     * Show success notification
     */
    success(message: string): HTMLElement {
        return this.show(message, 'success');
    }

    /**
     * Show error notification
     */
    error(message: string): HTMLElement {
        return this.show(message, 'error');
    }

    /**
     * Show warning notification
     */
    warning(message: string): HTMLElement {
        return this.show(message, 'warning');
    }

    /**
     * Show info notification
     */
    info(message: string): HTMLElement {
        return this.show(message, 'info');
    }

    /**
     * Clear all toasts
     */
    clearAll(): void {
        if (this.container) {
            const toasts = this.container.querySelectorAll('.alert');
            toasts.forEach(toast => {
                this.removeToast(toast as HTMLElement);
            });
        }
    }

    /**
     * Get toast count
     */
    getCount(): number {
        if (!this.container) return 0;
        return this.container.querySelectorAll('.alert').length;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).ToastNotification = ToastNotification;
}
