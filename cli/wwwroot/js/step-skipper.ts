/**
 * StepSkipper - Intelligent step skipping based on data and conditions
 * Automatically skips steps that should not be completed
 */

import { Stepper } from './stepper';
import { ConditionalStepManager } from './conditional-step-manager';

export interface SkipRule {
  stepIndex: number;
  rule: (data: any) => boolean;
}

export interface SkipEvent {
  stepIndex: number;
  reason: string;
  timestamp: number;
}

export class StepSkipper {
  private stepper: Stepper;
  private conditionalManager: ConditionalStepManager | null;
  private skipRules: Map<number, (data: any) => boolean>;
  private skippedSteps: Set<number>;
  private skipHistory: SkipEvent[];
  private maxHistorySize = 100;
  private onStepSkipped: ((event: SkipEvent) => void) | null = null;

  constructor(stepper: Stepper, conditionalManager?: ConditionalStepManager) {
    this.stepper = stepper;
    this.conditionalManager = conditionalManager || null;
    this.skipRules = new Map();
    this.skippedSteps = new Set();
    this.skipHistory = [];
  }

  /**
   * Add a skip rule for a step
   */
  addSkipRule(stepIndex: number, rule: (data: any) => boolean): StepSkipper {
    this.validateStepIndex(stepIndex);
    this.skipRules.set(stepIndex, rule);
    return this;
  }

  /**
   * Remove skip rule
   */
  removeSkipRule(stepIndex: number): StepSkipper {
    this.validateStepIndex(stepIndex);
    this.skipRules.delete(stepIndex);
    this.skippedSteps.delete(stepIndex);
    return this;
  }

  /**
   * Check if a step should be skipped
   */
  shouldSkipStep(stepIndex: number, data?: any): boolean {
    this.validateStepIndex(stepIndex);

    // First check if step is hidden
    if (this.conditionalManager && !this.conditionalManager.isStepVisible(stepIndex)) {
      return true;
    }

    // Then check skip rules
    if (this.skipRules.has(stepIndex) && data) {
      try {
        const rule = this.skipRules.get(stepIndex)!;
        return rule(data);
      } catch (error) {
        console.error(`Error evaluating skip rule for step ${stepIndex}:`, error);
        return false;
      }
    }

    return false;
  }

  /**
   * Get next visible, non-skipped step
   */
  getNextNavigableStep(fromIndex?: number, data?: any): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();
    const stepCount = this.stepper.getStepCount();

    for (let i = currentIndex + 1; i < stepCount; i++) {
      if (!this.shouldSkipStep(i, data)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Get previous visible, non-skipped step
   */
  getPreviousNavigableStep(fromIndex?: number, data?: any): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();

    for (let i = currentIndex - 1; i >= 0; i--) {
      if (!this.shouldSkipStep(i, data)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Auto-navigate past skipped steps
   */
  autoNavigatePastSkipped(data: any): boolean {
    const currentIndex = this.stepper.getCurrentStepIndex();

    if (this.shouldSkipStep(currentIndex, data)) {
      const nextStep = this.getNextNavigableStep(currentIndex, data);

      if (nextStep !== null) {
        this.recordSkip(currentIndex, 'auto-skip on navigation');
        return this.stepper.goToStep(nextStep);
      }
    }

    return false;
  }

  /**
   * Mark step as manually skipped
   */
  markSkipped(stepIndex: number, reason?: string): StepSkipper {
    this.validateStepIndex(stepIndex);
    this.skippedSteps.add(stepIndex);
    this.recordSkip(stepIndex, reason || 'manually marked');
    return this;
  }

  /**
   * Mark step as not skipped
   */
  markNotSkipped(stepIndex: number): StepSkipper {
    this.validateStepIndex(stepIndex);
    this.skippedSteps.delete(stepIndex);
    return this;
  }

  /**
   * Check if step was skipped
   */
  wasStepSkipped(stepIndex: number): boolean {
    this.validateStepIndex(stepIndex);
    return this.skippedSteps.has(stepIndex);
  }

  /**
   * Get all skipped steps
   */
  getSkippedSteps(): number[] {
    return Array.from(this.skippedSteps.values());
  }

  /**
   * Get count of navigable steps (visible and not skipped)
   */
  getNavigableStepCount(data?: any): number {
    let count = 0;
    const stepCount = this.stepper.getStepCount();

    for (let i = 0; i < stepCount; i++) {
      if (!this.shouldSkipStep(i, data)) {
        count++;
      }
    }

    return count;
  }

  /**
   * Record skip event
   */
  private recordSkip(stepIndex: number, reason: string): void {
    const event: SkipEvent = {
      stepIndex,
      reason,
      timestamp: Date.now()
    };

    this.skipHistory.push(event);

    // Limit history size
    if (this.skipHistory.length > this.maxHistorySize) {
      this.skipHistory.shift();
    }

    if (this.onStepSkipped) {
      this.onStepSkipped(event);
    }
  }

  /**
   * Get skip history
   */
  getSkipHistory(): SkipEvent[] {
    return [...this.skipHistory];
  }

  /**
   * Clear skip history
   */
  clearSkipHistory(): StepSkipper {
    this.skipHistory = [];
    return this;
  }

  /**
   * Set callback for skip events
   */
  onSkipped(callback: (event: SkipEvent) => void): StepSkipper {
    this.onStepSkipped = callback;
    return this;
  }

  /**
   * Get skip rules
   */
  getSkipRules(): Map<number, (data: any) => boolean> {
    return new Map(this.skipRules);
  }

  /**
   * Check if any steps are skipped
   */
  hasSkippedSteps(): boolean {
    return this.skippedSteps.size > 0;
  }

  /**
   * Get skip summary
   */
  getSkipSummary(): {
    totalSteps: number;
    skippedSteps: number;
    navigableSteps: number;
    rulesCount: number;
  } {
    const totalCount = this.stepper.getStepCount();
    const skippedCount = this.skippedSteps.size;

    return {
      totalSteps: totalCount,
      skippedSteps: skippedCount,
      navigableSteps: totalCount - skippedCount,
      rulesCount: this.skipRules.size
    };
  }

  /**
   * Reset all skip state
   */
  reset(): StepSkipper {
    this.skipRules.clear();
    this.skippedSteps.clear();
    this.skipHistory = [];
    return this;
  }

  /**
   * Validate step index
   */
  private validateStepIndex(stepIndex: number): void {
    const stepCount = this.stepper.getStepCount();
    if (stepIndex < 0 || stepIndex >= stepCount) {
      throw new Error(`Invalid step index: ${stepIndex} (max: ${stepCount - 1})`);
    }
  }
}
