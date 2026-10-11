/**
 * ConditionalStepManager - Manages conditional visibility of steps
 * Enables showing/hiding steps based on form data and custom rules
 */

import { Stepper } from './stepper';

export interface VisibilityRule {
  stepIndex: number;
  rule: (data: any) => boolean;
  dependsOn?: number[];
}

export interface VisibilityChange {
  stepIndex: number;
  wasVisible: boolean;
  isVisible: boolean;
}

export class ConditionalStepManager {
  private stepper: Stepper;
  private visibilityRules: Map<number, (data: any) => boolean>;
  private visibilityCache: Map<number, boolean>;
  private dependencyGraph: Map<number, Set<number>>;
  private onVisibilityChange: ((changes: VisibilityChange[]) => void) | null = null;

  constructor(stepper: Stepper) {
    this.stepper = stepper;
    this.visibilityRules = new Map();
    this.visibilityCache = new Map();
    this.dependencyGraph = new Map();
    this.initializeVisibility();
  }

  /**
   * Initialize all steps as visible
   */
  private initializeVisibility(): void {
    const stepCount = this.stepper.getStepCount();
    for (let i = 0; i < stepCount; i++) {
      this.visibilityCache.set(i, true);
    }
  }

  /**
   * Add a visibility rule for a step
   */
  addVisibilityRule(
    stepIndex: number,
    rule: (data: any) => boolean,
    dependsOn?: number[]
  ): ConditionalStepManager {
    this.validateStepIndex(stepIndex);
    this.visibilityRules.set(stepIndex, rule);

    if (dependsOn) {
      this.dependencyGraph.set(stepIndex, new Set(dependsOn));
    }

    return this;
  }

  /**
   * Remove visibility rule for a step
   */
  removeVisibilityRule(stepIndex: number): ConditionalStepManager {
    this.validateStepIndex(stepIndex);
    this.visibilityRules.delete(stepIndex);
    this.dependencyGraph.delete(stepIndex);
    this.visibilityCache.set(stepIndex, true);
    return this;
  }

  /**
   * Check if a step should be visible
   */
  isStepVisible(stepIndex: number): boolean {
    this.validateStepIndex(stepIndex);

    if (!this.visibilityRules.has(stepIndex)) {
      return true;
    }

    return this.visibilityCache.get(stepIndex) ?? true;
  }

  /**
   * Get all visible steps
   */
  getVisibleSteps(): number[] {
    const visibleSteps: number[] = [];
    const stepCount = this.stepper.getStepCount();

    for (let i = 0; i < stepCount; i++) {
      if (this.isStepVisible(i)) {
        visibleSteps.push(i);
      }
    }

    return visibleSteps;
  }

  /**
   * Get count of visible steps
   */
  getVisibleStepCount(): number {
    return this.getVisibleSteps().length;
  }

  /**
   * Update visibility based on current data
   */
  updateVisibility(data: any): VisibilityChange[] {
    const changes: VisibilityChange[] = [];

    for (const [stepIndex, rule] of this.visibilityRules.entries()) {
      const wasVisible = this.visibilityCache.get(stepIndex) ?? true;

      try {
        const isVisible = rule(data);
        if (wasVisible !== isVisible) {
          this.visibilityCache.set(stepIndex, isVisible);
          changes.push({
            stepIndex,
            wasVisible,
            isVisible
          });
        }
      } catch (error) {
        console.error(`Error evaluating visibility rule for step ${stepIndex}:`, error);
      }
    }

    if (changes.length > 0 && this.onVisibilityChange) {
      this.onVisibilityChange(changes);
    }

    return changes;
  }

  /**
   * Get visibility rules
   */
  getVisibilityRules(): Map<number, (data: any) => boolean> {
    return new Map(this.visibilityRules);
  }

  /**
   * Check if step has dependencies
   */
  hasDependencies(stepIndex: number): boolean {
    return this.dependencyGraph.has(stepIndex);
  }

  /**
   * Get step dependencies
   */
  getDependencies(stepIndex: number): Set<number> {
    return this.dependencyGraph.get(stepIndex) ?? new Set();
  }

  /**
   * Get steps that depend on this step
   */
  getDependents(stepIndex: number): number[] {
    const dependents: number[] = [];

    for (const [step, deps] of this.dependencyGraph.entries()) {
      if (deps.has(stepIndex)) {
        dependents.push(step);
      }
    }

    return dependents;
  }

  /**
   * Set callback for visibility changes
   */
  onVisibilityChanged(callback: (changes: VisibilityChange[]) => void): ConditionalStepManager {
    this.onVisibilityChange = callback;
    return this;
  }

  /**
   * Get next visible step from current position
   */
  getNextVisibleStep(fromIndex?: number): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();
    const visibleSteps = this.getVisibleSteps();

    for (let i = currentIndex + 1; i < this.stepper.getStepCount(); i++) {
      if (visibleSteps.includes(i)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Get previous visible step from current position
   */
  getPreviousVisibleStep(fromIndex?: number): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();
    const visibleSteps = this.getVisibleSteps();

    for (let i = currentIndex - 1; i >= 0; i--) {
      if (visibleSteps.includes(i)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Reset visibility rules
   */
  reset(): ConditionalStepManager {
    this.visibilityRules.clear();
    this.dependencyGraph.clear();
    this.initializeVisibility();
    return this;
  }

  /**
   * Export visibility state
   */
  exportVisibilityState(): string {
    const state = {
      rules: Array.from(this.visibilityRules.entries()).map(([index, _]) => ({
        stepIndex: index,
        hasRule: true
      })),
      visibility: Array.from(this.visibilityCache.entries())
    };

    return JSON.stringify(state);
  }

  /**
   * Get visibility summary
   */
  getVisibilitySummary(): {
    totalSteps: number;
    visibleSteps: number;
    hiddenSteps: number;
    rulesCount: number;
  } {
    const visibleCount = this.getVisibleStepCount();
    const totalCount = this.stepper.getStepCount();

    return {
      totalSteps: totalCount,
      visibleSteps: visibleCount,
      hiddenSteps: totalCount - visibleCount,
      rulesCount: this.visibilityRules.size
    };
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
