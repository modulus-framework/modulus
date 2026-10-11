/**
 * FormTemplateManager - Template management for multi-step forms
 * Create, save, load, and share form templates
 */

export interface FormTemplate {
  id: string;
  name: string;
  description?: string;
  definition: FormDefinition;
  createdBy: string;
  createdAt: number;
  updatedAt: number;
  isPublic: boolean;
  tags?: string[];
  category?: string;
}

export interface FormDefinition {
  steps: Step[];
  settings?: FormSettings;
  validation?: ValidationRules;
  workflow?: WorkflowDefinition;
}

export interface Step {
  id: string;
  title: string;
  description?: string;
  fields: Field[];
  optional?: boolean;
}

export interface Field {
  id: string;
  name: string;
  type: string;
  required?: boolean;
  defaultValue?: any;
  options?: any;
}

export interface FormSettings {
  layout?: 'horizontal' | 'vertical' | 'accordion' | 'carousel';
  theme?: string;
  requiresAuth?: boolean;
  autoSave?: boolean;
  [key: string]: any;
}

export interface ValidationRules {
  [key: string]: any;
}

export interface WorkflowDefinition {
  [key: string]: any;
}

export class FormTemplateManager {
  private templates: Map<string, FormTemplate>;
  private userTemplates: Map<string, Set<string>>;
  private sharedWith: Map<string, Set<string>>;
  private storageKey = 'form_templates';
  private maxTemplates = 1000;

  constructor() {
    this.templates = new Map();
    this.userTemplates = new Map();
    this.sharedWith = new Map();
    this.loadFromStorage();
  }

  /**
   * Create a new template
   */
  createTemplate(
    name: string,
    definition: FormDefinition,
    userId: string,
    options?: { description?: string; isPublic?: boolean; tags?: string[] }
  ): FormTemplate {
    const templateId = `tpl_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;

    const template: FormTemplate = {
      id: templateId,
      name,
      description: options?.description,
      definition,
      createdBy: userId,
      createdAt: Date.now(),
      updatedAt: Date.now(),
      isPublic: options?.isPublic ?? false,
      tags: options?.tags
    };

    this.templates.set(templateId, template);

    if (!this.userTemplates.has(userId)) {
      this.userTemplates.set(userId, new Set());
    }
    this.userTemplates.get(userId)!.add(templateId);

    this.persistToStorage();
    return template;
  }

  /**
   * Update template
   */
  updateTemplate(
    templateId: string,
    updates: Partial<FormTemplate>,
    userId: string
  ): FormTemplate | null {
    const template = this.templates.get(templateId);

    if (!template) {
      return null;
    }

    // Only creator can update
    if (template.createdBy !== userId) {
      throw new Error('Only creator can update template');
    }

    const updated: FormTemplate = {
      ...template,
      ...updates,
      id: template.id,
      createdBy: template.createdBy,
      createdAt: template.createdAt,
      updatedAt: Date.now()
    };

    this.templates.set(templateId, updated);
    this.persistToStorage();
    return updated;
  }

  /**
   * Get template by ID
   */
  getTemplate(templateId: string): FormTemplate | null {
    return this.templates.get(templateId) || null;
  }

  /**
   * Get user's templates
   */
  getUserTemplates(userId: string): FormTemplate[] {
    const templateIds = this.userTemplates.get(userId) || new Set();
    return Array.from(templateIds)
      .map(id => this.templates.get(id))
      .filter((t): t is FormTemplate => t !== undefined);
  }

  /**
   * Delete template
   */
  deleteTemplate(templateId: string, userId: string): boolean {
    const template = this.templates.get(templateId);

    if (!template) {
      return false;
    }

    if (template.createdBy !== userId) {
      throw new Error('Only creator can delete template');
    }

    this.templates.delete(templateId);

    const userTemplates = this.userTemplates.get(userId);
    if (userTemplates) {
      userTemplates.delete(templateId);
    }

    this.persistToStorage();
    return true;
  }

  /**
   * Share template with user
   */
  shareTemplate(templateId: string, userId: string, sharedWithUserId: string): boolean {
    const template = this.templates.get(templateId);

    if (!template) {
      return false;
    }

    if (template.createdBy !== userId) {
      throw new Error('Only creator can share template');
    }

    if (!this.sharedWith.has(templateId)) {
      this.sharedWith.set(templateId, new Set());
    }

    this.sharedWith.get(templateId)!.add(sharedWithUserId);
    this.persistToStorage();
    return true;
  }

  /**
   * Get templates shared with user
   */
  getSharedTemplates(userId: string): FormTemplate[] {
    const shared: FormTemplate[] = [];

    for (const [templateId, sharedUsers] of this.sharedWith.entries()) {
      if (sharedUsers.has(userId)) {
        const template = this.templates.get(templateId);
        if (template) {
          shared.push(template);
        }
      }
    }

    return shared;
  }

  /**
   * List all public templates
   */
  getPublicTemplates(): FormTemplate[] {
    return Array.from(this.templates.values()).filter(t => t.isPublic);
  }

  /**
   * Search templates by name or tags
   */
  searchTemplates(query: string, userId?: string): FormTemplate[] {
    const lowerQuery = query.toLowerCase();
    let templates = Array.from(this.templates.values());

    // Filter by user if specified
    if (userId) {
      const userIds = this.userTemplates.get(userId) || new Set();
      const sharedIds = new Set<string>();

      for (const [templateId, sharedUsers] of this.sharedWith.entries()) {
        if (sharedUsers.has(userId)) {
          sharedIds.add(templateId);
        }
      }

      templates = templates.filter(
        t => userIds.has(t.id) || sharedIds.has(t.id) || t.isPublic
      );
    } else {
      templates = templates.filter(t => t.isPublic);
    }

    // Search by name or tags
    return templates.filter(t =>
      t.name.toLowerCase().includes(lowerQuery) ||
      t.tags?.some(tag => tag.toLowerCase().includes(lowerQuery))
    );
  }

  /**
   * Get templates by category
   */
  getTemplatesByCategory(category: string, userId?: string): FormTemplate[] {
    let templates = Array.from(this.templates.values())
      .filter(t => t.category === category);

    if (userId) {
      const userIds = this.userTemplates.get(userId) || new Set();
      const sharedIds = new Set<string>();

      for (const [templateId, sharedUsers] of this.sharedWith.entries()) {
        if (sharedUsers.has(userId)) {
          sharedIds.add(templateId);
        }
      }

      templates = templates.filter(
        t => userIds.has(t.id) || sharedIds.has(t.id) || t.isPublic
      );
    } else {
      templates = templates.filter(t => t.isPublic);
    }

    return templates;
  }

  /**
   * Export template as JSON
   */
  exportTemplate(templateId: string): string | null {
    const template = this.templates.get(templateId);
    if (!template) {
      return null;
    }

    return JSON.stringify(template, null, 2);
  }

  /**
   * Import template from JSON
   */
  importTemplate(json: string, userId: string): FormTemplate | null {
    try {
      const data = JSON.parse(json);

      // Validate structure
      if (!data.name || !data.definition) {
        throw new Error('Invalid template structure');
      }

      // Create with new ID
      const template = this.createTemplate(
        data.name,
        data.definition,
        userId,
        {
          description: data.description,
          tags: data.tags
        }
      );

      return template;
    } catch (error) {
      console.error('Failed to import template:', error);
      return null;
    }
  }

  /**
   * Get template count
   */
  getTemplateCount(): number {
    return this.templates.size;
  }

  /**
   * Get total templates for user
   */
  getUserTemplateCount(userId: string): number {
    return (this.userTemplates.get(userId) || new Set()).size;
  }

  /**
   * Clone template
   */
  cloneTemplate(templateId: string, userId: string): FormTemplate | null {
    const original = this.templates.get(templateId);

    if (!original) {
      return null;
    }

    return this.createTemplate(
      `${original.name} (Copy)`,
      JSON.parse(JSON.stringify(original.definition)),
      userId,
      {
        description: original.description,
        tags: original.tags
      }
    );
  }

  /**
   * Get all categories
   */
  getAllCategories(): string[] {
    const categories = new Set<string>();

    for (const template of this.templates.values()) {
      if (template.category) {
        categories.add(template.category);
      }
    }

    return Array.from(categories);
  }

  /**
   * Persist to storage
   */
  private persistToStorage(): void {
    try {
      const data = {
        templates: Array.from(this.templates.values()),
        userTemplates: Object.fromEntries(this.userTemplates),
        sharedWith: Object.fromEntries(this.sharedWith)
      };

      localStorage.setItem(this.storageKey, JSON.stringify(data));
    } catch (error) {
      console.error('Failed to persist templates:', error);
    }
  }

  /**
   * Load from storage
   */
  private loadFromStorage(): void {
    try {
      const data = localStorage.getItem(this.storageKey);

      if (!data) {
        return;
      }

      const parsed = JSON.parse(data);

      // Load templates
      for (const template of parsed.templates || []) {
        this.templates.set(template.id, template);
      }

      // Load user templates
      for (const [userId, templateIds] of Object.entries(parsed.userTemplates || {})) {
        this.userTemplates.set(userId, new Set(templateIds as string[]));
      }

      // Load shared with
      for (const [templateId, userIds] of Object.entries(parsed.sharedWith || {})) {
        this.sharedWith.set(templateId, new Set(userIds as string[]));
      }
    } catch (error) {
      console.error('Failed to load templates:', error);
    }
  }

  /**
   * Clear all templates
   */
  clearAll(): void {
    this.templates.clear();
    this.userTemplates.clear();
    this.sharedWith.clear();
    localStorage.removeItem(this.storageKey);
  }
}
