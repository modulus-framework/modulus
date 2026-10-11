/**
 * ValidationSummary - Display summary of all validation errors
 * Shows errors grouped by field with links to jump to fields
 */

import { ValidationResponse, FieldError } from './form-submission-handler';

export interface ValidationSummaryOptions {
    containerSelector?: string;
    dismissible?: boolean;
    groupByField?: boolean;
    showWarnings?: boolean;
    autoCreate?: boolean;
    className?: string;
}

export class ValidationSummary {
    private container: HTMLElement | null = null;
    private options: ValidationSummaryOptions;
    private formElement: HTMLFormElement;
    private errors: FieldError[] = [];

    constructor(
        formElement: HTMLFormElement | string,
        options: ValidationSummaryOptions = {}
    ) {
        this.formElement = typeof formElement === 'string'
            ? (document.querySelector(formElement) as HTMLFormElement)
            : formElement;

        this.options = {
            containerSelector: '.validation-summary-container',
            dismissible: true,
            groupByField: true,
            showWarnings: false,
            autoCreate: true,
            className: 'validation-summary',
            ...options
        };

        this.initialize();
    }

    /**
     * Initialize validation summary
     */
    private initialize(): void {
        if (this.options.containerSelector) {
            this.container = this.formElement.querySelector(this.options.containerSelector) as HTMLElement;
        }

        if (!this.container && this.options.autoCreate) {
            this.createContainer();
        }
    }

    /**
     * Create summary container if it doesn't exist
     */
    private createContainer(): void {
        this.container = document.createElement('div');
        this.container.className = this.options.className!;
        this.container.style.display = 'none';
        this.formElement.insertBefore(this.container, this.formElement.firstChild);
    }

    /**
     * Display validation response errors
     */
    displayResponse(response: ValidationResponse): void {
        this.errors = response.errors;

        if (response.isValid) {
            this.hide();
            return;
        }

        this.render();
        this.show();
    }

    /**
     * Render summary
     */
    private render(): void {
        if (!this.container) return;

        this.container.innerHTML = '';

        if (this.errors.length === 0) {
            this.hide();
            return;
        }

        // Create alert container
        const alert = document.createElement('div');
        alert.className = 'alert alert-danger alert-dismissible fade show';
        alert.setAttribute('role', 'alert');

        // Title
        const title = document.createElement('h4');
        title.className = 'alert-heading';
        title.textContent = 'Please correct the following errors:';
        alert.appendChild(title);

        // Error list
        const list = document.createElement('ul');
        list.className = 'validation-summary-list mb-0';

        if (this.options.groupByField) {
            this.renderGroupedErrors(list);
        } else {
            this.renderFlatErrors(list);
        }

        alert.appendChild(list);

        // Dismiss button
        if (this.options.dismissible) {
            const closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.className = 'btn-close';
            closeBtn.setAttribute('aria-label', 'Close');
            closeBtn.addEventListener('click', () => this.hide());
            alert.appendChild(closeBtn);
        }

        this.container!.appendChild(alert);
    }

    /**
     * Render grouped errors
     */
    private renderGroupedErrors(list: HTMLUListElement): void {
        const grouped = new Map<string, FieldError[]>();

        this.errors.forEach(error => {
            const errors = grouped.get(error.field) || [];
            errors.push(error);
            grouped.set(error.field, errors);
        });

        grouped.forEach((errors, fieldName) => {
            const fieldItem = document.createElement('li');
            fieldItem.className = 'validation-summary-field';

            // Field name as link
            const link = document.createElement('a');
            link.href = `#${fieldName}`;
            link.textContent = this.formatFieldName(fieldName);
            link.style.cursor = 'pointer';
            link.style.textDecoration = 'underline';
            link.addEventListener('click', (e) => {
                e.preventDefault();
                this.focusField(fieldName);
            });
            fieldItem.appendChild(link);

            // Error messages
            if (errors.length > 1) {
                const errorList = document.createElement('ul');
                errorList.className = 'validation-summary-errors ms-3 mt-1';
                errors.forEach(error => {
                    const errorItem = document.createElement('li');
                    errorItem.textContent = error.message;
                    errorList.appendChild(errorItem);
                });
                fieldItem.appendChild(errorList);
            } else if (errors.length === 1) {
                const message = document.createElement('span');
                message.className = 'ms-2';
                message.textContent = errors[0].message;
                fieldItem.appendChild(message);
            }

            list.appendChild(fieldItem);
        });
    }

    /**
     * Render flat errors
     */
    private renderFlatErrors(list: HTMLUListElement): void {
        this.errors.forEach(error => {
            const item = document.createElement('li');
            item.className = 'validation-summary-item';

            if (error.field !== 'general') {
                const label = document.createElement('strong');
                label.textContent = this.formatFieldName(error.field);
                label.className = 'me-2';
                item.appendChild(label);
            }

            const message = document.createElement('span');
            message.textContent = error.message;
            item.appendChild(message);

            list.appendChild(item);
        });
    }

    /**
     * Format field name for display
     */
    private formatFieldName(fieldName: string): string {
        if (fieldName === 'general') return 'General Error';

        return fieldName
            .replace(/([A-Z])/g, ' $1')
            .replace(/^./, str => str.toUpperCase())
            .trim();
    }

    /**
     * Focus field and scroll into view
     */
    private focusField(fieldName: string): void {
        const field = this.formElement.querySelector(`[name="${fieldName}"]`) as
            HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

        if (field) {
            field.focus();
            field.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    /**
     * Show summary
     */
    show(): void {
        if (this.container) {
            this.container.style.display = 'block';
        }
    }

    /**
     * Hide summary
     */
    hide(): void {
        if (this.container) {
            this.container.style.display = 'none';
        }
    }

    /**
     * Clear summary
     */
    clear(): void {
        this.errors = [];
        if (this.container) {
            this.container.innerHTML = '';
            this.container.style.display = 'none';
        }
    }

    /**
     * Get error count
     */
    getErrorCount(): number {
        return this.errors.length;
    }

    /**
     * Get errors
     */
    getErrors(): FieldError[] {
        return this.errors;
    }

    /**
     * Get errors for field
     */
    getFieldErrors(fieldName: string): FieldError[] {
        return this.errors.filter(e =>
            e.field.toLowerCase() === fieldName.toLowerCase()
        );
    }

    /**
     * Check if has errors
     */
    hasErrors(): boolean {
        return this.errors.length > 0;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).ValidationSummary = ValidationSummary;
}
