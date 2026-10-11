/**
 * WorkflowEngine - Branching workflow logic for multi-path forms
 * Enables different paths based on user selections and data
 */

import { Stepper } from './stepper';
import { ConditionalStepManager } from './conditional-step-manager';
import { StepSkipper } from './step-skipper';

export interface WorkflowBranch {
  name: string;
  condition: (data: any) => boolean;
  path: number[];
}

export interface WorkflowDefinition {
  name: string;
  branches: WorkflowBranch[];
  mergeStep?: number;
  description?: string;
}

export interface WorkflowState {
  currentWorkflow: string;
  activeBranch: string;
  completedPath: number[];
  currentPath: number[];
}

export class WorkflowEngine {
  private stepper: Stepper;
  private conditionalManager: ConditionalStepManager | null;
  private skipperManager: StepSkipper | null;
  private workflows: Map<string, WorkflowDefinition>;
  private activeWorkflow: string | null = null;
  private activeBranch: string | null = null;
  private completedPath: number[] = [];
  private onWorkflowChange: ((workflow: WorkflowState) => void) | null = null;
  private onBranchChange: ((branch: string) => void) | null = null;

  constructor(
    stepper: Stepper,
    conditionalManager?: ConditionalStepManager,
    skipperManager?: StepSkipper
  ) {
    this.stepper = stepper;
    this.conditionalManager = conditionalManager || null;
    this.skipperManager = skipperManager || null;
    this.workflows = new Map();
  }

  /**
   * Define a workflow
   */
  defineWorkflow(definition: WorkflowDefinition): WorkflowEngine {
    if (!definition.name || !definition.branches) {
      throw new Error('Workflow must have name and branches');
    }

    this.workflows.set(definition.name, definition);
    return this;
  }

  /**
   * Add a branch to a workflow
   */
  addBranch(
    workflowName: string,
    branchName: string,
    condition: (data: any) => boolean,
    path: number[]
  ): WorkflowEngine {
    const workflow = this.workflows.get(workflowName);

    if (!workflow) {
      throw new Error(`Workflow not found: ${workflowName}`);
    }

    workflow.branches.push({
      name: branchName,
      condition,
      path
    });

    return this;
  }

  /**
   * Evaluate workflow and get path for data
   */
  evaluateWorkflow(workflowName: string, data: any): number[] | null {
    const workflow = this.workflows.get(workflowName);

    if (!workflow) {
      throw new Error(`Workflow not found: ${workflowName}`);
    }

    for (const branch of workflow.branches) {
      try {
        if (branch.condition(data)) {
          this.activeBranch = branch.name;
          this.activeWorkflow = workflowName;

          if (this.onBranchChange) {
            this.onBranchChange(branch.name);
          }

          return branch.path;
        }
      } catch (error) {
        console.error(`Error evaluating branch ${branch.name}:`, error);
      }
    }

    return null;
  }

  /**
   * Activate workflow for data
   */
  activateWorkflow(workflowName: string, data: any): boolean {
    const path = this.evaluateWorkflow(workflowName, data);

    if (path) {
      this.currentPath = path;
      this.updateVisibilityForPath(path);

      if (this.onWorkflowChange) {
        this.onWorkflowChange(this.getWorkflowState());
      }

      return true;
    }

    return false;
  }

  /**
   * Get current workflow
   */
  getCurrentWorkflow(): string | null {
    return this.activeWorkflow;
  }

  /**
   * Get current branch
   */
  getCurrentBranch(): string | null {
    return this.activeBranch;
  }

  /**
   * Get current active path
   */
  getActivePath(): number[] {
    return [...this.currentPath];
  }

  /**
   * Get next step in current path
   */
  getNextPathStep(fromIndex?: number): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();

    for (let i = currentIndex + 1; i < this.stepper.getStepCount(); i++) {
      if (this.currentPath.includes(i)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Get previous step in current path
   */
  getPreviousPathStep(fromIndex?: number): number | null {
    const currentIndex = fromIndex ?? this.stepper.getCurrentStepIndex();

    for (let i = currentIndex - 1; i >= 0; i--) {
      if (this.currentPath.includes(i)) {
        return i;
      }
    }

    return null;
  }

  /**
   * Check if step is in active path
   */
  isStepInPath(stepIndex: number): boolean {
    return this.currentPath.includes(stepIndex);
  }

  /**
   * Mark step as completed in current workflow
   */
  completeStep(stepIndex: number): WorkflowEngine {
    if (this.isStepInPath(stepIndex) && !this.completedPath.includes(stepIndex)) {
      this.completedPath.push(stepIndex);
    }

    return this;
  }

  /**
   * Get completed steps in current workflow
   */
  getCompletedSteps(): number[] {
    return [...this.completedPath];
  }

  /**
   * Get completion progress (0-100%)
   */
  getProgress(): number {
    if (this.currentPath.length === 0) {
      return 0;
    }

    const completed = this.completedPath.filter(step => this.currentPath.includes(step)).length;
    return Math.round((completed / this.currentPath.length) * 100);
  }

  /**
   * Check if all steps in path are completed
   */
  isPathComplete(): boolean {
    return this.currentPath.every(step => this.completedPath.includes(step));
  }

  /**
   * Get list of available workflows
   */
  getAvailableWorkflows(): string[] {
    return Array.from(this.workflows.keys());
  }

  /**
   * Get workflow definition
   */
  getWorkflowDefinition(workflowName: string): WorkflowDefinition | null {
    return this.workflows.get(workflowName) || null;
  }

  /**
   * Get all branches in workflow
   */
  getWorkflowBranches(workflowName: string): WorkflowBranch[] {
    const workflow = this.workflows.get(workflowName);
    return workflow ? [...workflow.branches] : [];
  }

  /**
   * Merge branches back to common step
   */
  mergeBranches(mergeStepIndex: number): WorkflowEngine {
    if (mergeStepIndex < 0 || mergeStepIndex >= this.stepper.getStepCount()) {
      throw new Error(`Invalid merge step: ${mergeStepIndex}`);
    }

    // Add merge step to current path if not already there
    if (!this.currentPath.includes(mergeStepIndex)) {
      this.currentPath.push(mergeStepIndex);
    }

    return this;
  }

  /**
   * Update visibility based on active path
   */
  private updateVisibilityForPath(path: number[]): void {
    if (!this.conditionalManager) return;

    // Hide steps not in path
    const totalSteps = this.stepper.getStepCount();

    for (let i = 0; i < totalSteps; i++) {
      const shouldBeVisible = path.includes(i);

      if (!shouldBeVisible) {
        this.conditionalManager.addVisibilityRule(i, () => false);
      }
    }

    this.conditionalManager.updateVisibility({});
  }

  /**
   * Get current workflow state
   */
  getWorkflowState(): WorkflowState {
    return {
      currentWorkflow: this.activeWorkflow || '',
      activeBranch: this.activeBranch || '',
      completedPath: [...this.completedPath],
      currentPath: [...this.currentPath]
    };
  }

  /**
   * Set workflow change callback
   */
  onWorkflowChanged(callback: (state: WorkflowState) => void): WorkflowEngine {
    this.onWorkflowChange = callback;
    return this;
  }

  /**
   * Set branch change callback
   */
  onBranchChanged(callback: (branch: string) => void): WorkflowEngine {
    this.onBranchChange = callback;
    return this;
  }

  /**
   * Export workflow configuration
   */
  exportWorkflows(): Record<string, WorkflowDefinition> {
    const exported: Record<string, WorkflowDefinition> = {};

    for (const [name, workflow] of this.workflows.entries()) {
      exported[name] = { ...workflow };
    }

    return exported;
  }

  /**
   * Reset workflow state
   */
  reset(): WorkflowEngine {
    this.activeWorkflow = null;
    this.activeBranch = null;
    this.completedPath = [];
    this.currentPath = [];
    return this;
  }

  /**
   * Current path (internal)
   */
  private currentPath: number[] = [];
}
