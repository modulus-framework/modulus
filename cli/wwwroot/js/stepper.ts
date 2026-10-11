/**
 * Stepper - Multi-step form wizard component
 * Manages step navigation, progress tracking, and step state
 */

interface StepConfig {
  id: string;
  title: string;
  description?: string;
  element?: HTMLElement;
  optional?: boolean;
  skippable?: boolean;
}

interface StepState {
  id: string;
  title: string;
  complete: boolean;
  visited: boolean;
  valid?: boolean;
  errorCount?: number;
}

type StepLayout = 'horizontal' | 'vertical';
type StepStatus = 'incomplete' | 'current' | 'complete' | 'error';

class Step {
  id: string;
  title: string;
  description: string;
  element?: HTMLElement;
  optional: boolean;
  skippable: boolean;
  state: StepState;

  constructor(config: StepConfig) {
    this.id = config.id;
    this.title = config.title;
    this.description = config.description || '';
    this.element = config.element;
    this.optional = config.optional || false;
    this.skippable = config.skippable || false;
    this.state = {
      id: config.id,
      title: config.title,
      complete: false,
      visited: false,
      valid: true,
      errorCount: 0
    };
  }

  markComplete(): void {
    this.state.complete = true;
  }

  markVisited(): void {
    this.state.visited = true;
  }

  markInvalid(errorCount: number = 1): void {
    this.state.valid = false;
    this.state.errorCount = errorCount;
  }

  reset(): void {
    this.state.complete = false;
    this.state.visited = false;
    this.state.valid = true;
    this.state.errorCount = 0;
  }
}

class Stepper {
  private steps: Step[] = [];
  private currentStepIndex: number = 0;
  private containerElement: HTMLElement;
  private layout: StepLayout = 'horizontal';
  private onStepChange?: (oldIndex: number, newIndex: number) => void;
  private onStepComplete?: (stepIndex: number) => void;
  private onProgressChange?: (progress: number) => void;

  constructor(containerElement: HTMLElement, steps: StepConfig[] = [], options?: {
    layout?: StepLayout;
    onStepChange?: (oldIndex: number, newIndex: number) => void;
    onStepComplete?: (stepIndex: number) => void;
    onProgressChange?: (progress: number) => void;
  }) {
    this.containerElement = containerElement;
    this.layout = options?.layout || 'horizontal';
    this.onStepChange = options?.onStepChange;
    this.onStepComplete = options?.onStepComplete;
    this.onProgressChange = options?.onProgressChange;

    if (steps.length > 0) {
      steps.forEach(config => this.addStep(config));
      this.renderStepper();
    }
  }

  addStep(config: StepConfig): Stepper {
    const step = new Step(config);
    this.steps.push(step);
    return this;
  }

  nextStep(): boolean {
    if (this.canMoveToNextStep()) {
      const oldIndex = this.currentStepIndex;
      this.currentStepIndex++;
      this.steps[oldIndex].markComplete();
      this.steps[this.currentStepIndex].markVisited();
      this.renderStepper();
      this.onStepChange?.(oldIndex, this.currentStepIndex);
      return true;
    }
    return false;
  }

  previousStep(): void {
    if (this.currentStepIndex > 0) {
      const oldIndex = this.currentStepIndex;
      this.currentStepIndex--;
      this.steps[this.currentStepIndex].markVisited();
      this.renderStepper();
      this.onStepChange?.(oldIndex, this.currentStepIndex);
    }
  }

  goToStep(index: number): boolean {
    if (index >= 0 && index < this.steps.length) {
      // Can only skip forward if all previous steps are complete
      for (let i = 0; i < index; i++) {
        if (!this.steps[i].state.complete && !this.steps[i].skippable) {
          return false;
        }
      }

      const oldIndex = this.currentStepIndex;
      this.currentStepIndex = index;
      this.steps[index].markVisited();
      this.renderStepper();
      this.onStepChange?.(oldIndex, index);
      return true;
    }
    return false;
  }

  canMoveToNextStep(): boolean {
    if (this.currentStepIndex >= this.steps.length - 1) {
      return false;
    }
    const currentStep = this.steps[this.currentStepIndex];
    return currentStep.state.valid || currentStep.optional;
  }

  canMoveToPreviousStep(): boolean {
    return this.currentStepIndex > 0;
  }

  isLastStep(): boolean {
    return this.currentStepIndex === this.steps.length - 1;
  }

  getCurrentStep(): Step {
    return this.steps[this.currentStepIndex];
  }

  getCurrentStepIndex(): number {
    return this.currentStepIndex;
  }

  getStep(index: number): Step | undefined {
    return this.steps[index];
  }

  getSteps(): Step[] {
    return this.steps;
  }

  getStepCount(): number {
    return this.steps.length;
  }

  getProgress(): number {
    const completed = this.steps.filter(s => s.state.complete).length;
    return Math.round((completed / this.steps.length) * 100);
  }

  getStepStatus(index: number): StepStatus {
    if (index < 0 || index >= this.steps.length) return 'incomplete';
    const step = this.steps[index];

    if (!step.state.valid && step.state.visited) return 'error';
    if (index === this.currentStepIndex) return 'current';
    if (step.state.complete) return 'complete';
    return 'incomplete';
  }

  markStepComplete(index: number): void {
    if (index >= 0 && index < this.steps.length) {
      this.steps[index].markComplete();
      this.onStepComplete?.(index);
      this.updateProgress();
      this.renderStepper();
    }
  }

  markStepInvalid(index: number, errorCount: number = 1): void {
    if (index >= 0 && index < this.steps.length) {
      this.steps[index].markInvalid(errorCount);
      this.renderStepper();
    }
  }

  markStepValid(index: number): void {
    if (index >= 0 && index < this.steps.length) {
      this.steps[index].state.valid = true;
      this.steps[index].state.errorCount = 0;
      this.renderStepper();
    }
  }

  reset(): void {
    this.steps.forEach(step => step.reset());
    this.currentStepIndex = 0;
    this.renderStepper();
  }

  private updateProgress(): void {
    const progress = this.getProgress();
    this.onProgressChange?.(progress);
  }

  private renderStepper(): void {
    // Render step indicators
    const stepList = this.containerElement.querySelector('.stepper-steps') ||
      this.createStepIndicators();

    // Render step content
    const stepContent = this.containerElement.querySelector('.stepper-content') ||
      this.createStepContent();

    // Update active states
    this.updateStepIndicators(stepList);
    this.updateStepContent(stepContent);

    this.updateProgress();
  }

  private createStepIndicators(): HTMLElement {
    const div = document.createElement('div');
    div.className = `stepper stepper-${this.layout}`;

    const ol = document.createElement('ol');
    ol.className = 'stepper-steps';

    this.steps.forEach((step, index) => {
      const li = document.createElement('li');
      li.className = `stepper-step`;
      li.setAttribute('data-step', index.toString());

      const status = this.getStepStatus(index);
      li.classList.add(`stepper-step-${status}`);

      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'stepper-button';
      button.innerHTML = `<span class="stepper-number">${index + 1}</span>`;

      const label = document.createElement('div');
      label.className = 'stepper-label';
      label.innerHTML = `<div class="stepper-title">${step.title}</div>`;
      if (step.description) {
        label.innerHTML += `<div class="stepper-description">${step.description}</div>`;
      }

      button.addEventListener('click', () => this.goToStep(index));

      li.appendChild(button);
      li.appendChild(label);
      ol.appendChild(li);
    });

    div.appendChild(ol);
    this.containerElement.appendChild(div);

    return ol;
  }

  private createStepContent(): HTMLElement {
    const div = document.createElement('div');
    div.className = 'stepper-content';

    this.containerElement.appendChild(div);
    return div;
  }

  private updateStepIndicators(stepList: HTMLElement): void {
    stepList.querySelectorAll('.stepper-step').forEach((li, index) => {
      li.classList.remove('stepper-step-incomplete', 'stepper-step-current',
        'stepper-step-complete', 'stepper-step-error');

      const status = this.getStepStatus(index);
      li.classList.add(`stepper-step-${status}`);
    });
  }

  private updateStepContent(stepContent: HTMLElement): void {
    // Clear content
    stepContent.innerHTML = '';

    const currentStep = this.getCurrentStep();
    if (currentStep.element) {
      stepContent.appendChild(currentStep.element);
    }
  }
}

export { Stepper, Step, StepConfig, StepState, StepStatus, StepLayout };
