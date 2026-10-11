/**
 * DynamicStepHandler - Manage steps dynamically at runtime
 * Add, remove, and reorder steps while maintaining state consistency
 */

import { Stepper, Step } from './stepper';
import { FormStateManager } from './form-state-manager';
import { ConditionalStepManager } from './conditional-step-manager';

export interface StepModificationEvent {
  type: 'add' | 'remove' | 'reorder';
  stepIndex?: number;
  newIndex?: number;
  step?: Step;
  timestamp: number;
}

export class DynamicStepHandler {
  private stepper: Stepper;
  private stateManager: FormStateManager | null;
  private conditionalManager: ConditionalStepManager | null;
  private dynamicStepIndices: Set<number>;
  private modificationHistory: StepModificationEvent[];
  private maxHistorySize = 100;
  private onStepModified: ((event: StepModificationEvent) => void) | null = null;

  constructor(
    stepper: Stepper,
    stateManager?: FormStateManager,
    conditionalManager?: ConditionalStepManager
  ) {
    this.stepper = stepper;
    this.stateManager = stateManager || null;
    this.conditionalManager = conditionalManager || null;
    this.dynamicStepIndices = new Set();
    this.modificationHistory = [];
  }

  /**
   * Add a step at specified index
   */
  addStep(index: number, step: Step): DynamicStepHandler {
    if (index < 0 || index > this.stepper.getStepCount()) {
      throw new Error(`Invalid index: ${index}`);
    }

    if (!step.id || !step.title) {
      throw new Error('Step must have id and title');
    }

    // Get current steps
    const currentSteps = this.getAllSteps();

    // Insert new step
    const updatedSteps = [
      ...currentSteps.slice(0, index),
      step,
      ...currentSteps.slice(index)
    ];

    // Update stepper
    this.updateStepperSteps(updatedSteps);

    // Mark as dynamic
    this.dynamicStepIndices.add(index);

    // Shift indices for steps after insertion
    const newDynamicIndices = new Set<number>();
    for (const idx of this.dynamicStepIndices) {
      if (idx >= index && idx !== index) {
        newDynamicIndices.add(idx + 1);
      } else {
        newDynamicIndices.add(idx);
      }
    }
    this.dynamicStepIndices = newDynamicIndices;

    this.recordModification({
      type: 'add',
      stepIndex: index,
      step: step
    });

    return this;
  }

  /**
   * Remove step at specified index
   */
  removeStep(index: number): DynamicStepHandler {
    const stepCount = this.stepper.getStepCount();

    if (index < 0 || index >= stepCount) {
      throw new Error(`Invalid index: ${index}`);
    }

    if (!this.isDynamicStep(index)) {
      console.warn(`Removing static step at index ${index}`);
    }

    // Get current steps
    const currentSteps = this.getAllSteps();

    // Remove step
    const updatedSteps = currentSteps.filter((_, i) => i !== index);

    // Save state before removal
    if (this.stateManager) {
      const stepData = this.stateManager.getState(`step_${index}`);
      if (stepData) {
        this.stateManager.setState(`removed_step_${index}`, stepData);
      }
    }

    // Update stepper
    this.updateStepperSteps(updatedSteps);

    // Update dynamic indices
    const newDynamicIndices = new Set<number>();
    for (const idx of this.dynamicStepIndices) {
      if (idx > index) {
        newDynamicIndices.add(idx - 1);
      } else if (idx < index) {
        newDynamicIndices.add(idx);
      }
    }
    this.dynamicStepIndices = newDynamicIndices;
    this.dynamicStepIndices.delete(index);

    this.recordModification({
      type: 'remove',
      stepIndex: index
    });

    return this;
  }

  /**
   * Reorder steps
   */
  reorderSteps(newOrder: number[]): DynamicStepHandler {
    const stepCount = this.stepper.getStepCount();

    // Validate order
    if (newOrder.length !== stepCount) {
      throw new Error('Order array must contain all step indices');
    }

    if (new Set(newOrder).size !== stepCount) {
      throw new Error('Order array must contain unique indices');
    }

    const currentSteps = this.getAllSteps();
    const reorderedSteps = newOrder.map(i => currentSteps[i]);

    // Update stepper
    this.updateStepperSteps(reorderedSteps);

    // Create reverse mapping
    const indexMapping = new Map<number, number>();
    for (let i = 0; i < newOrder.length; i++) {
      indexMapping.set(newOrder[i], i);
    }

    // Update state keys if using state manager
    if (this.stateManager) {
      const allState = this.stateManager.getAllState();
      const updatedState: any = {};

      for (const [key, value] of Object.entries(allState)) {
        if (key.startsWith('step_')) {
          const oldIdx = parseInt(key.split('_')[1]);
          if (indexMapping.has(oldIdx)) {
            const newIdx = indexMapping.get(oldIdx);
            updatedState[`step_${newIdx}`] = value;
          }
        } else {
          updatedState[key] = value;
        }
      }

      this.stateManager.mergeState(updatedState);
    }

    this.recordModification({
      type: 'reorder',
      newIndex: 0 // Reorder event doesn't have specific indices
    });

    return this;
  }

  /**
   * Get all steps
   */
  getSteps(): Step[] {
    return this.getAllSteps();
  }

  /**
   * Get step count
   */
  getStepCount(): number {
    return this.stepper.getStepCount();
  }

  /**
   * Check if step is dynamic
   */
  isDynamicStep(index: number): boolean {
    return this.dynamicStepIndices.has(index);
  }

  /**
   * Get dynamic step indices
   */
  getDynamicStepIndices(): number[] {
    return Array.from(this.dynamicStepIndices).sort((a, b) => a - b);
  }

  /**
   * Get static step indices
   */
  getStaticStepIndices(): number[] {
    const staticIndices: number[] = [];
    const stepCount = this.stepper.getStepCount();

    for (let i = 0; i < stepCount; i++) {
      if (!this.dynamicStepIndices.has(i)) {
        staticIndices.push(i);
      }
    }

    return staticIndices;
  }

  /**
   * Swap two steps
   */
  swapSteps(index1: number, index2: number): DynamicStepHandler {
    const stepCount = this.stepper.getStepCount();

    if (index1 < 0 || index1 >= stepCount || index2 < 0 || index2 >= stepCount) {
      throw new Error('Invalid step indices');
    }

    const steps = this.getAllSteps();
    [steps[index1], steps[index2]] = [steps[index2], steps[index1]];

    this.updateStepperSteps(steps);

    return this;
  }

  /**
   * Move step from one position to another
   */
  moveStep(fromIndex: number, toIndex: number): DynamicStepHandler {
    const stepCount = this.stepper.getStepCount();

    if (fromIndex < 0 || fromIndex >= stepCount || toIndex < 0 || toIndex >= stepCount) {
      throw new Error('Invalid step indices');
    }

    const steps = this.getAllSteps();
    const [step] = steps.splice(fromIndex, 1);
    steps.splice(toIndex, 0, step);

    this.updateStepperSteps(steps);

    return this;
  }

  /**
   * Set callback for step modifications
   */
  onModified(callback: (event: StepModificationEvent) => void): DynamicStepHandler {
    this.onStepModified = callback;
    return this;
  }

  /**
   * Get modification history
   */
  getModificationHistory(): StepModificationEvent[] {
    return [...this.modificationHistory];
  }

  /**
   * Clear modification history
   */
  clearModificationHistory(): DynamicStepHandler {
    this.modificationHistory = [];
    return this;
  }

  /**
   * Get modification summary
   */
  getModificationSummary(): {
    totalModifications: number;
    addedSteps: number;
    removedSteps: number;
    reorderedCount: number;
    dynamicStepCount: number;
  } {
    let addedCount = 0;
    let removedCount = 0;
    let reorderedCount = 0;

    for (const event of this.modificationHistory) {
      if (event.type === 'add') addedCount++;
      else if (event.type === 'remove') removedCount++;
      else if (event.type === 'reorder') reorderedCount++;
    }

    return {
      totalModifications: this.modificationHistory.length,
      addedSteps: addedCount,
      removedSteps: removedCount,
      reorderedCount: reorderedCount,
      dynamicStepCount: this.dynamicStepIndices.size
    };
  }

  /**
   * Record modification event
   */
  private recordModification(event: Omit<StepModificationEvent, 'timestamp'>): void {
    const fullEvent: StepModificationEvent = {
      ...event,
      timestamp: Date.now()
    };

    this.modificationHistory.push(fullEvent);

    if (this.modificationHistory.length > this.maxHistorySize) {
      this.modificationHistory.shift();
    }

    if (this.onStepModified) {
      this.onStepModified(fullEvent);
    }
  }

  /**
   * Update stepper steps (internal method)
   */
  private updateStepperSteps(steps: Step[]): void {
    // Recreate stepper with new steps
    const container = this.stepper.getContainer();
    const options = this.stepper.getOptions();

    // Clear existing stepper
    this.stepper.reset();

    // This is a workaround - we need to recreate the stepper
    // In a real implementation, Stepper would have an updateSteps method
    // For now, we'll store steps internally
    (this.stepper as any).steps = steps;
    (this.stepper as any).stepCount = steps.length;
  }

  /**
   * Get all steps (internal method)
   */
  private getAllSteps(): Step[] {
    // Access internal steps from stepper
    return (this.stepper as any).steps || [];
  }

  /**
   * Reset all modifications
   */
  reset(): DynamicStepHandler {
    this.dynamicStepIndices.clear();
    this.modificationHistory = [];
    return this;
  }
}
