/**
 * CrossFieldValidator - Validate relationships between multiple fields
 * Examples: password confirmation, date ranges, dependent fields
 */

import { ValidationError } from './field-validators';

export interface CrossFieldRule {
    field1: string;
    field2: string;
    rule: (value1: any, value2: any) => boolean;
    message: string;
    code?: string;
}

export class CrossFieldValidator {
    private rules: CrossFieldRule[] = [];
    private errors: Map<string, ValidationError> = new Map();

    /**
     * Add a cross-field validation rule
     */
    addRule(
        field1: string,
        field2: string,
        rule: (value1: any, value2: any) => boolean,
        message: string,
        code: string = 'CROSS_FIELD_ERROR'
    ): CrossFieldValidator {
        this.rules.push({
            field1,
            field2,
            rule,
            message,
            code
        });
        return this;
    }

    /**
     * Add password confirmation rule
     */
    addPasswordConfirmation(passwordField: string = 'password', confirmField: string = 'confirmPassword'): CrossFieldValidator {
        return this.addRule(
            passwordField,
            confirmField,
            (pwd, confirm) => pwd === confirm,
            'Passwords do not match',
            'PASSWORD_MISMATCH'
        );
    }

    /**
     * Add date range rule
     */
    addDateRange(startField: string, endField: string): CrossFieldValidator {
        return this.addRule(
            startField,
            endField,
            (start, end) => {
                const startDate = new Date(start);
                const endDate = new Date(end);
                return startDate < endDate;
            },
            'End date must be after start date',
            'INVALID_DATE_RANGE'
        );
    }

    /**
     * Add matching fields rule
     */
    addMatching(field1: string, field2: string, message?: string): CrossFieldValidator {
        return this.addRule(
            field1,
            field2,
            (val1, val2) => val1 === val2,
            message || `${field1} and ${field2} must match`,
            'FIELD_MISMATCH'
        );
    }

    /**
     * Validate form data against all rules
     */
    validate(formData: Record<string, any>): ValidationError[] {
        this.errors.clear();
        const errors: ValidationError[] = [];

        this.rules.forEach(rule => {
            const value1 = formData[rule.field1];
            const value2 = formData[rule.field2];

            // Skip validation if either field is empty
            if (!value1 || !value2) {
                return;
            }

            // Check rule
            if (!rule.rule(value1, value2)) {
                const error: ValidationError = {
                    field: rule.field1,
                    message: rule.message,
                    code: rule.code || 'CROSS_FIELD_ERROR',
                    severity: 'error'
                };

                this.errors.set(rule.field1, error);
                errors.push(error);
            }
        });

        return errors;
    }

    /**
     * Validate specific field pair
     */
    validatePair(field1: string, value1: any, field2: string, value2: any): ValidationError | null {
        const rule = this.rules.find(r => r.field1 === field1 && r.field2 === field2);

        if (!rule) {
            return null;
        }

        if (!value1 || !value2) {
            return null;
        }

        if (!rule.rule(value1, value2)) {
            return {
                field: field1,
                message: rule.message,
                code: rule.code || 'CROSS_FIELD_ERROR',
                severity: 'error'
            };
        }

        return null;
    }

    /**
     * Get error for field
     */
    getFieldError(fieldName: string): ValidationError | undefined {
        return this.errors.get(fieldName);
    }

    /**
     * Get all errors
     */
    getErrors(): ValidationError[] {
        return Array.from(this.errors.values());
    }

    /**
     * Check if field has error
     */
    hasError(fieldName: string): boolean {
        return this.errors.has(fieldName);
    }

    /**
     * Clear errors
     */
    clearErrors(): void {
        this.errors.clear();
    }

    /**
     * Clear specific field error
     */
    clearFieldError(fieldName: string): void {
        this.errors.delete(fieldName);
    }
}

/**
 * Conditional Field Validator - Show/hide and validate based on conditions
 */
export interface FieldCondition {
    fieldName: string;
    triggerField: string;
    triggerValue: any;
    action: 'show' | 'hide' | 'enable' | 'disable' | 'require' | 'optional';
}

export class ConditionalFieldValidator {
    private conditions: FieldCondition[] = [];

    /**
     * Add condition for field visibility/state
     */
    addCondition(
        triggerField: string,
        triggerValue: any,
        targetField: string,
        action: 'show' | 'hide' | 'enable' | 'disable' | 'require' | 'optional' = 'show'
    ): ConditionalFieldValidator {
        this.conditions.push({
            fieldName: targetField,
            triggerField,
            triggerValue,
            action
        });
        return this;
    }

    /**
     * Apply all conditions based on form data
     */
    applyConditions(formElement: HTMLFormElement, formData: Record<string, any>): void {
        this.conditions.forEach(condition => {
            const triggerValue = formData[condition.triggerField];
            const isConditionMet = this.evaluateCondition(triggerValue, condition.triggerValue);

            const targetField = formElement.querySelector(`[name="${condition.fieldName}"]`) as
                HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

            if (!targetField) return;

            switch (condition.action) {
                case 'show':
                    targetField.style.display = isConditionMet ? '' : 'none';
                    if (isConditionMet) {
                        targetField.parentElement?.style.display = '';
                    } else {
                        targetField.parentElement?.style.display = 'none';
                    }
                    break;

                case 'hide':
                    targetField.style.display = !isConditionMet ? '' : 'none';
                    if (!isConditionMet) {
                        targetField.parentElement?.style.display = '';
                    } else {
                        targetField.parentElement?.style.display = 'none';
                    }
                    break;

                case 'enable':
                    targetField.disabled = !isConditionMet;
                    break;

                case 'disable':
                    targetField.disabled = isConditionMet;
                    break;

                case 'require':
                    if (isConditionMet) {
                        targetField.setAttribute('data-required', 'true');
                        targetField.required = true;
                    } else {
                        targetField.removeAttribute('data-required');
                        targetField.required = false;
                    }
                    break;

                case 'optional':
                    if (isConditionMet) {
                        targetField.removeAttribute('data-required');
                        targetField.required = false;
                    } else {
                        targetField.setAttribute('data-required', 'true');
                        targetField.required = true;
                    }
                    break;
            }
        });
    }

    /**
     * Evaluate if condition is met
     */
    private evaluateCondition(triggerValue: any, expectedValue: any): boolean {
        if (typeof expectedValue === 'boolean') {
            return Boolean(triggerValue) === expectedValue;
        }

        if (typeof expectedValue === 'string') {
            return String(triggerValue).toLowerCase() === String(expectedValue).toLowerCase();
        }

        if (Array.isArray(expectedValue)) {
            return expectedValue.includes(triggerValue);
        }

        return triggerValue === expectedValue;
    }

    /**
     * Watch for changes and apply conditions
     */
    watchChanges(formElement: HTMLFormElement): void {
        formElement.addEventListener('change', (e) => {
            const target = e.target as HTMLInputElement;
            const formData = this.getFormData(formElement);
            this.applyConditions(formElement, formData);
        });

        // Apply initial conditions
        const formData = this.getFormData(formElement);
        this.applyConditions(formElement, formData);
    }

    /**
     * Get form data as object
     */
    private getFormData(formElement: HTMLFormElement): Record<string, any> {
        const formData = new FormData(formElement);
        const data: Record<string, any> = {};

        formData.forEach((value, key) => {
            if (data[key]) {
                if (!Array.isArray(data[key])) {
                    data[key] = [data[key]];
                }
                data[key].push(value);
            } else {
                data[key] = value;
            }
        });

        return data;
    }
}

// Export for global use
if (typeof window !== 'undefined') {
    (window as any).CrossFieldValidator = CrossFieldValidator;
    (window as any).ConditionalFieldValidator = ConditionalFieldValidator;
}
