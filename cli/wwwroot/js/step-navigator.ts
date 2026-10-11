/**
 * StepNavigator - Navigation controls for multi-step forms
 * Manages Previous/Next/Submit button states and interactions
 */

import { Stepper } from './stepper';

interface NavigatorConfig {
  previousButtonSelector?: string;
  nextButtonSelector?: string;
  submitButtonSelector?: string;
  onPrevious?: () => void;
  onNext?: () => boolean | Promise<boolean>;
  onSubmit?: () => void | Promise<void>;
  autoUpdateButtonStates?: boolean;
}

class StepNavigator {
  private stepper: Stepper;
  private previousButton?: HTMLButtonElement;
  private nextButton?: HTMLButtonElement;
  private submitButton?: HTMLButtonElement;
  private onPrevious?: () => void;
  private onNext?: () => boolean | Promise<boolean>;
  private onSubmit?: () => void | Promise<void>;
  private autoUpdateStates: boolean = true;
  private isNavigating: boolean = false;

  constructor(stepper: Stepper, config?: NavigatorConfig) {
    this.stepper = stepper;
    this.onPrevious = config?.onPrevious;
    this.onNext = config?.onNext;
    this.onSubmit = config?.onSubmit;
    this.autoUpdateStates = config?.autoUpdateButtonStates !== false;

    if (config?.previousButtonSelector) {
      this.previousButton = document.querySelector(config.previousButtonSelector) || undefined;
    }
    if (config?.nextButtonSelector) {
      this.nextButton = document.querySelector(config.nextButtonSelector) || undefined;
    }
    if (config?.submitButtonSelector) {
      this.submitButton = document.querySelector(config.submitButtonSelector) || undefined;
    }

    this.attachEventListeners();
    this.updateButtonStates();
  }

  private attachEventListeners(): void {
    if (this.previousButton) {
      this.previousButton.addEventListener('click', () => this.handlePreviousClick());
    }

    if (this.nextButton) {
      this.nextButton.addEventListener('click', () => this.handleNextClick());
    }

    if (this.submitButton) {
      this.submitButton.addEventListener('click', () => this.handleSubmitClick());
    }

    // Listen for stepper changes to update button states
    if (this.autoUpdateStates) {
      document.addEventListener('stepper-step-change', () => this.updateButtonStates());
    }
  }

  private async handlePreviousClick(): Promise<void> {
    if (this.isNavigating) return;
    if (!this.stepper.canMoveToPreviousStep()) return;

    this.isNavigating = true;
    this.previousButton?.setAttribute('disabled', 'disabled');

    try {
      this.onPrevious?.();
      this.stepper.previousStep();
      this.updateButtonStates();
    } finally {
      this.isNavigating = false;
      this.previousButton?.removeAttribute('disabled');
    }
  }

  private async handleNextClick(): Promise<void> {
    if (this.isNavigating) return;
    if (!this.stepper.canMoveToNextStep()) return;

    this.isNavigating = true;
    this.nextButton?.setAttribute('disabled', 'disabled');

    try {
      // Allow external validation via onNext callback
      const canProceed = await this.onNext?.() !== false;

      if (canProceed && this.stepper.nextStep()) {
        this.updateButtonStates();
      }
    } finally {
      this.isNavigating = false;
      this.nextButton?.removeAttribute('disabled');
    }
  }

  private async handleSubmitClick(): Promise<void> {
    if (this.isNavigating) return;
    if (!this.stepper.isLastStep()) return;

    this.isNavigating = true;
    this.submitButton?.setAttribute('disabled', 'disabled');

    try {
      await this.onSubmit?.();
    } finally {
      this.isNavigating = false;
      this.submitButton?.removeAttribute('disabled');
    }
  }

  updateButtonStates(): void {
    const canPrevious = this.stepper.canMoveToPreviousStep();
    const canNext = this.stepper.canMoveToNextStep();
    const isLastStep = this.stepper.isLastStep();

    // Update Previous button
    if (this.previousButton) {
      if (canPrevious) {
        this.previousButton.removeAttribute('disabled');
        this.previousButton.classList.remove('disabled');
      } else {
        this.previousButton.setAttribute('disabled', 'disabled');
        this.previousButton.classList.add('disabled');
      }
    }

    // Update Next button
    if (this.nextButton) {
      if (canNext && !isLastStep) {
        this.nextButton.removeAttribute('disabled');
        this.nextButton.classList.remove('disabled');
      } else {
        this.nextButton.setAttribute('disabled', 'disabled');
        this.nextButton.classList.add('disabled');
      }

      // Show submit on last step
      if (isLastStep) {
        this.nextButton.style.display = 'none';
      } else {
        this.nextButton.style.display = '';
      }
    }

    // Update Submit button
    if (this.submitButton) {
      if (isLastStep) {
        this.submitButton.style.display = '';
        this.submitButton.removeAttribute('disabled');
      } else {
        this.submitButton.style.display = 'none';
        this.submitButton.setAttribute('disabled', 'disabled');
      }
    }
  }

  setPreviousButton(selector: string): StepNavigator {
    this.previousButton = document.querySelector(selector) || undefined;
    if (this.previousButton) {
      this.previousButton.addEventListener('click', () => this.handlePreviousClick());
    }
    this.updateButtonStates();
    return this;
  }

  setNextButton(selector: string): StepNavigator {
    this.nextButton = document.querySelector(selector) || undefined;
    if (this.nextButton) {
      this.nextButton.addEventListener('click', () => this.handleNextClick());
    }
    this.updateButtonStates();
    return this;
  }

  setSubmitButton(selector: string): StepNavigator {
    this.submitButton = document.querySelector(selector) || undefined;
    if (this.submitButton) {
      this.submitButton.addEventListener('click', () => this.handleSubmitClick());
    }
    this.updateButtonStates();
    return this;
  }

  setOnPrevious(callback: () => void): StepNavigator {
    this.onPrevious = callback;
    return this;
  }

  setOnNext(callback: () => boolean | Promise<boolean>): StepNavigator {
    this.onNext = callback;
    return this;
  }

  setOnSubmit(callback: () => void | Promise<void>): StepNavigator {
    this.onSubmit = callback;
    return this;
  }

  goToStep(index: number): boolean {
    if (this.stepper.goToStep(index)) {
      this.updateButtonStates();
      return true;
    }
    return false;
  }

  getCurrentStepIndex(): number {
    return this.stepper.getCurrentStepIndex();
  }

  getTotalSteps(): number {
    return this.stepper.getStepCount();
  }

  isNavigatingInProgress(): boolean {
    return this.isNavigating;
  }
}

export { StepNavigator, NavigatorConfig };
