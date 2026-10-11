/**
 * ValidationMessages - Localized error message handling
 * Supports custom messages and message templates
 */

export interface MessageTemplate {
    [code: string]: (fieldName: string, params?: any) => string;
}

export class ValidationMessages {
    private messages: Map<string, string> = new Map();
    private templates: MessageTemplate = {
        REQUIRED: (fieldName) => `${fieldName} is required`,
        INVALID_FORMAT: (fieldName) => `${fieldName} has an invalid format`,
        LENGTH_ERROR: (fieldName, params) =>
            params?.min && params?.max
                ? `${fieldName} must be between ${params.min} and ${params.max} characters`
                : params?.min
                ? `${fieldName} must be at least ${params.min} characters`
                : `${fieldName} must be at most ${params.max} characters`,
        RANGE_ERROR: (fieldName, params) =>
            params?.min && params?.max
                ? `${fieldName} must be between ${params.min} and ${params.max}`
                : params?.min
                ? `${fieldName} must be greater than or equal to ${params.min}`
                : `${fieldName} must be less than or equal to ${params.max}`,
        EMAIL_ERROR: (fieldName) => `${fieldName} must be a valid email address`,
        URL_ERROR: (fieldName) => `${fieldName} must be a valid URL`,
        CUSTOM_ERROR: (fieldName) => `${fieldName} is invalid`,
    };
    private culture: string = 'en-US';

    constructor(culture: string = 'en-US') {
        this.culture = culture;
    }

    /**
     * Get localized message for error code
     */
    getMessageForCode(code: string, fieldName: string, params?: any): string {
        // Check custom messages first
        const customKey = `${code}_${fieldName}`;
        if (this.messages.has(customKey)) {
            return this.messages.get(customKey)!;
        }

        // Check generic custom messages
        if (this.messages.has(code)) {
            return this.messages.get(code)!;
        }

        // Use template
        const template = this.templates[code];
        if (template) {
            return template(fieldName, params);
        }

        // Fallback
        return `${fieldName} is invalid`;
    }

    /**
     * Set custom message for error code
     */
    setMessage(code: string, message: string): void {
        this.messages.set(code, message);
    }

    /**
     * Set custom message for specific field and code
     */
    setFieldMessage(code: string, fieldName: string, message: string): void {
        this.messages.set(`${code}_${fieldName}`, message);
    }

    /**
     * Set custom message template
     */
    setTemplate(code: string, template: (fieldName: string, params?: any) => string): void {
        this.templates[code] = template;
    }

    /**
     * Load messages from object
     */
    loadMessages(messages: { [key: string]: string }): void {
        Object.entries(messages).forEach(([code, message]) => {
            this.messages.set(code, message);
        });
    }

    /**
     * Get all messages
     */
    getAllMessages(): { [key: string]: string } {
        const result: { [key: string]: string } = {};
        this.messages.forEach((value, key) => {
            result[key] = value;
        });
        return result;
    }

    /**
     * Clear all custom messages
     */
    clear(): void {
        this.messages.clear();
    }

    /**
     * Set culture for localization
     */
    setCulture(culture: string): void {
        this.culture = culture;
        this.updateTemplatesForCulture(culture);
    }

    /**
     * Update templates based on culture
     */
    private updateTemplatesForCulture(culture: string): void {
        // This method can be extended to load culture-specific templates
        // from external files or APIs
        switch (culture) {
            case 'es-ES':
                this.templates.REQUIRED = (fieldName) => `${fieldName} es requerido`;
                this.templates.INVALID_FORMAT = (fieldName) => `${fieldName} tiene un formato inválido`;
                this.templates.EMAIL_ERROR = (fieldName) => `${fieldName} debe ser una dirección de correo válida`;
                break;

            case 'fr-FR':
                this.templates.REQUIRED = (fieldName) => `${fieldName} est requis`;
                this.templates.INVALID_FORMAT = (fieldName) => `${fieldName} a un format invalide`;
                this.templates.EMAIL_ERROR = (fieldName) => `${fieldName} doit être une adresse e-mail valide`;
                break;

            case 'de-DE':
                this.templates.REQUIRED = (fieldName) => `${fieldName} ist erforderlich`;
                this.templates.INVALID_FORMAT = (fieldName) => `${fieldName} hat ein ungültiges Format`;
                this.templates.EMAIL_ERROR = (fieldName) => `${fieldName} muss eine gültige E-Mail-Adresse sein`;
                break;

            default:
                // English (default)
                this.templates.REQUIRED = (fieldName) => `${fieldName} is required`;
                this.templates.INVALID_FORMAT = (fieldName) => `${fieldName} has an invalid format`;
                this.templates.EMAIL_ERROR = (fieldName) => `${fieldName} must be a valid email address`;
                break;
        }
    }
}

/**
 * Helper function to format error message
 */
export function formatErrorMessage(
    code: string,
    fieldName: string,
    params?: any,
    messages?: ValidationMessages
): string {
    if (!messages) {
        messages = new ValidationMessages();
    }
    return messages.getMessageForCode(code, fieldName, params);
}

/**
 * Helper function to display toast notification
 */
export function showToast(message: string, type: 'success' | 'error' | 'warning' | 'info' = 'info'): void {
    // Bootstrap toast implementation
    const toastElement = document.createElement('div');
    toastElement.className = `alert alert-${type === 'error' ? 'danger' : type} alert-dismissible fade show`;
    toastElement.setAttribute('role', 'alert');
    toastElement.innerHTML = `
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    `;

    const container = document.querySelector('.toast-container') || document.body;
    container.appendChild(toastElement);

    // Auto-dismiss after 5 seconds
    setTimeout(() => {
        toastElement.remove();
    }, 5000);
}

/**
 * Helper function to highlight field errors
 */
export function highlightFieldError(
    fieldElement: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
    errorMessage: string,
    errorClass: string = 'is-invalid'
): void {
    fieldElement.classList.add(errorClass);

    // Create and append error message element
    const errorDiv = document.createElement('div');
    errorDiv.className = 'invalid-feedback d-block';
    errorDiv.textContent = errorMessage;

    const fieldGroup = fieldElement.closest('.form-group') || fieldElement.parentElement;
    if (fieldGroup) {
        fieldGroup.appendChild(errorDiv);
    }
}

/**
 * Helper function to clear field errors
 */
export function clearFieldError(
    fieldElement: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
    errorClass: string = 'is-invalid',
    successClass: string = 'is-valid'
): void {
    fieldElement.classList.remove(errorClass);
    fieldElement.classList.add(successClass);

    // Remove error message elements
    const fieldGroup = fieldElement.closest('.form-group') || fieldElement.parentElement;
    if (fieldGroup) {
        const errorDivs = fieldGroup.querySelectorAll('.invalid-feedback');
        errorDivs.forEach(el => el.remove());
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).ValidationMessages = ValidationMessages;
    (window as any).formatErrorMessage = formatErrorMessage;
    (window as any).showToast = showToast;
    (window as any).highlightFieldError = highlightFieldError;
    (window as any).clearFieldError = clearFieldError;
}
