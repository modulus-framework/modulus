/**
 * StepValidator - Multi-step form validation
 * Handles step-level, cross-step, and progressive validation
 */

import { Stepper } from './stepper';
import { FormValidator } from './form-validator';

interface StepValidationRule {
  stepIndex: number;
  validator: (data: any) => boolean | Promise<boolean>;
  errorMessage: string;
}

interface CrossStepRule {
  sourceSteps: number[];
  targetStep: number;
  validator: (data: any) => boolean | Promise<boolean>;
  errorMessage: string;
}

interface ValidationError {
  stepIndex: number;
  fieldName?: string;
  message: string;
  severity: 'error' | 'warning' | 'info';
}

class StepValidator {
  private stepper: Stepper;
  private formValidator?: FormValidator;
  private stepRules: StepValidationRule[] = [];
  private crossStepRules: CrossStepRule[] = [];
  private stepErrors: Map<number, ValidationError[]> = new Map();
  private validatedSteps: Set<number> = new Set();
  private onValidationError?: (errors: ValidationError[]) => void;
  private onValidationSuccess?: (stepIndex: number) => void;

  constructor(stepper: Stepper, formValidator?: FormValidator, options?: {
    onValidationError?: (errors: ValidationError[]) => void;
    onValidationSuccess?: (stepIndex: number) => void;
  }) {
    this.stepper = stepper;
    this.formValidator = formValidator;
    this.onValidationError = options?.onValidationError;
    this.onValidationSuccess = options?.onValidationSuccess;
  }

  /**
   * Add validation rule for a specific step
   */
  addStepRule(stepIndex: number, validator: (data: any) => boolean | Promise<boolean>,
              errorMessage: string): StepValidator {
    this.stepRules.push({
      stepIndex,
      validator,
      errorMessage
    });
    return this;
  }

  /**
   * Add cross-step validation rule
   */
  addCrossStepRule(sourceSteps: number[], targetStep: number,
                   validator: (data: any) => boolean | Promise<boolean>,
                   errorMessage: string): StepValidator {
    this.crossStepRules.push({
      sourceSteps,
      targetStep,
      validator,
      errorMessage
    });
    return this;
  }

  /**
   * Validate current step before progression
   */
  async validateCurrentStep(): Promise<boolean> {
    const stepIndex = this.stepper.getCurrentStepIndex();
    return this.validateStep(stepIndex);
  }

  /**
   * Validate specific step
   */
  async validateStep(stepIndex: number): Promise<boolean> {
    if (stepIndex < 0 || stepIndex >= this.stepper.getStepCount()) {
      return false;
    }

    const errors: ValidationError[] = [];

    // Validate form fields in this step
    const stepForm = this.getStepForm(stepIndex);
    if (stepForm && this.formValidator) {
      const fieldErrors = this.formValidator.validateAll();
      errors.push(...fieldErrors.map((err: any) => ({
        stepIndex,
        fieldName: err.fieldName,
        message: err.message,
        severity: 'error' as const
      })));
    }

    // Validate step-specific rules
    const stepRulesForThis = this.stepRules.filter(r => r.stepIndex === stepIndex);
    for (const rule of stepRulesForThis) {
      try {
        const data = this.getStepData(stepIndex);
        const isValid = await rule.validator(data);

        if (!isValid) {
          errors.push({
            stepIndex,
            message: rule.errorMessage,
            severity: 'error'
          });
        }
      } catch (error) {
        errors.push({
          stepIndex,
          message: `Validation error: ${error}`,
          severity: 'error'
        });
      }
    }

    // Store errors
    if (errors.length > 0) {
      this.stepErrors.set(stepIndex, errors);
      this.stepper.markStepInvalid(stepIndex, errors.length);
      this.onValidationError?.(errors);
      return false;
    } else {
      this.stepErrors.delete(stepIndex);
      this.stepper.markStepValid(stepIndex);
      this.validatedSteps.add(stepIndex);
      this.onValidationSuccess?.(stepIndex);
      return true;
    }
  }

  /**
   * Validate before moving to next step
   */
  async validateForNextStep(): Promise<boolean> {
    const currentIndex = this.stepper.getCurrentStepIndex();

    // Validate current step
    const isCurrentValid = await this.validateStep(currentIndex);
    if (!isCurrentValid) return false;

    // Check cross-step rules that apply to next step
    const nextIndex = currentIndex + 1;
    const applicableCrossRules = this.crossStepRules.filter(
      r => r.sourceSteps.includes(currentIndex) && r.targetStep === nextIndex
    );

    for (const rule of applicableCrossRules) {
      try {
        const allStepData = this.getAllStepData(rule.sourceSteps);
        const isValid = await rule.validator(allStepData);

        if (!isValid) {
          const error: ValidationError = {
            stepIndex: currentIndex,
            message: rule.errorMessage,
            severity: 'error'
          };
          this.stepErrors.set(currentIndex, [
            ...(this.stepErrors.get(currentIndex) || []),
            error
          ]);
          this.onValidationError?.([error]);
          return false;
        }
      } catch (error) {
        console.error('Cross-step validation error:', error);
        return false;
      }
    }

    return true;
  }

  /**
   * Validate entire form before submission
   */
  async validateAllSteps(): Promise<boolean> {
    const allErrors: ValidationError[] = [];
    const totalSteps = this.stepper.getStepCount();

    for (let i = 0; i < totalSteps; i++) {
      const isValid = await this.validateStep(i);
      if (!isValid) {
        const errors = this.stepErrors.get(i) || [];
        allErrors.push(...errors);
      }
    }

    if (allErrors.length > 0) {
      this.onValidationError?.(allErrors);
      return false;
    }

    return true;
  }

  /**
   * Get validation errors for a step
   */
  getStepErrors(stepIndex: number): ValidationError[] {
    return this.stepErrors.get(stepIndex) || [];
  }

  /**
   * Get all validation errors
   */
  getAllErrors(): ValidationError[] {
    const all: ValidationError[] = [];
    this.stepErrors.forEach(errors => all.push(...errors));
    return all;
  }

  /**
   * Check if step has errors
   */
  hasStepErrors(stepIndex: number): boolean {
    return this.stepErrors.has(stepIndex) &&
           this.stepErrors.get(stepIndex)!.length > 0;
  }

  /**
   * Clear errors for a step
   */
  clearStepErrors(stepIndex: number): void {
    this.stepErrors.delete(stepIndex);
    this.stepper.markStepValid(stepIndex);
  }

  /**
   * Clear all errors
   */
  clearAllErrors(): void {
    this.stepErrors.clear();
    for (let i = 0; i < this.stepper.getStepCount(); i++) {
      this.stepper.markStepValid(i);
    }
  }

  /**
   * Check if step has been validated
   */
  isStepValidated(stepIndex: number): boolean {
    return this.validatedSteps.has(stepIndex);
  }

  /**
   * Get validated steps count
   */
  getValidatedStepsCount(): number {
    return this.validatedSteps.size;
  }

  /**
   * Reset validation state
   */
  reset(): void {
    this.stepErrors.clear();
    this.validatedSteps.clear();
  }

  /**
   * Get data for a specific step
   */
  private getStepData(stepIndex: number): any {
    const form = this.getStepForm(stepIndex);
    if (!form) return {};

    const data: any = {};
    const inputs = form.querySelectorAll('input, select, textarea');

    inputs.forEach((input: Element) => {
      const field = input as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
      if (field.name) {
        if (field.type === 'checkbox' || field.type === 'radio') {
          data[field.name] = (field as HTMLInputElement).checked;
        } else {
          data[field.name] = field.value;
        }
      }
    });

    return data;
  }

  /**
   * Get data for multiple steps
   */
  private getAllStepData(stepIndices: number[]): any {
    const combined: any = {};

    stepIndices.forEach(index => {
      const stepData = this.getStepData(index);
      Object.assign(combined, stepData);
    });

    return combined;
  }

  /**
   * Get form element for a step
   */
  private getStepForm(stepIndex: number): HTMLFormElement | null {
    const step = this.stepper.getStep(stepIndex);
    if (!step || !step.element) return null;

    const form = step.element.querySelector('form');
    return form || (step.element as any instanceof HTMLFormElement ? step.element : null);
  }

  /**
   * Export validation state as JSON
   */
  exportValidationState(): string {
    const state = {
      stepErrors: Array.from(this.stepErrors.entries()),
      validatedSteps: Array.from(this.validatedSteps),
      totalSteps: this.stepper.getStepCount()
    };
    return JSON.stringify(state);
  }

  /**
   * Import validation state from JSON
   */
  importValidationState(jsonString: string): boolean {
    try {
      const state = JSON.parse(jsonString);

      this.stepErrors.clear();
      state.stepErrors.forEach(([stepIndex, errors]: [number, ValidationError[]]) => {
        this.stepErrors.set(stepIndex, errors);
      });

      this.validatedSteps.clear();
      state.validatedSteps.forEach((stepIndex: number) => {
        this.validatedSteps.add(stepIndex);
      });

      return true;
    } catch (error) {
      console.error('Failed to import validation state:', error);
      return false;
    }
  }

  /**
   * Get validation summary
   */
  getValidationSummary(): {
    totalErrors: number;
    errorsByStep: Map<number, number>;
    validationProgress: number;
    allValid: boolean;
  } {
    const errorsByStep = new Map<number, number>();
    let totalErrors = 0;

    this.stepErrors.forEach((errors, stepIndex) => {
      errorsByStep.set(stepIndex, errors.length);
      totalErrors += errors.length;
    });

    const validationProgress = this.validatedSteps.size / this.stepper.getStepCount();

    return {
      totalErrors,
      errorsByStep,
      validationProgress,
      allValid: totalErrors === 0
    };
  }
}

export {
  StepValidator,
  StepValidationRule,
  CrossStepRule,
  ValidationError
};
