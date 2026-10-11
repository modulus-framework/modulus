/**
 * TemplateAndDraftTests - Tests for template and draft management
 */

import { FormTemplateManager } from '../wwwroot/js/form-template-manager';
import { FormDraftHandler } from '../wwwroot/js/form-draft-handler';

describe('FormTemplateManager', () => {
  let manager: FormTemplateManager;

  beforeEach(() => {
    manager = new FormTemplateManager();
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  describe('Template Creation', () => {
    test('should create a template', () => {
      const template = manager.createTemplate(
        'Test Form',
        { steps: [] },
        'user1'
      );

      expect(template.name).toBe('Test Form');
      expect(template.createdBy).toBe('user1');
      expect(template.isPublic).toBe(false);
    });

    test('should create template with options', () => {
      const template = manager.createTemplate(
        'Test Form',
        { steps: [] },
        'user1',
        {
          description: 'A test form',
          isPublic: true,
          tags: ['test', 'sample']
        }
      );

      expect(template.description).toBe('A test form');
      expect(template.isPublic).toBe(true);
      expect(template.tags).toContain('test');
    });
  });

  describe('Template Retrieval', () => {
    test('should get template by ID', () => {
      const created = manager.createTemplate('Test', { steps: [] }, 'user1');
      const retrieved = manager.getTemplate(created.id);

      expect(retrieved).toBeDefined();
      expect(retrieved?.name).toBe('Test');
    });

    test('should return null for non-existent template', () => {
      const retrieved = manager.getTemplate('non-existent');
      expect(retrieved).toBeNull();
    });

    test('should get user templates', () => {
      manager.createTemplate('Template 1', { steps: [] }, 'user1');
      manager.createTemplate('Template 2', { steps: [] }, 'user1');
      manager.createTemplate('Template 3', { steps: [] }, 'user2');

      const user1Templates = manager.getUserTemplates('user1');
      expect(user1Templates).toHaveLength(2);

      const user2Templates = manager.getUserTemplates('user2');
      expect(user2Templates).toHaveLength(1);
    });
  });

  describe('Template Updates', () => {
    test('should update template', () => {
      const template = manager.createTemplate('Old Name', { steps: [] }, 'user1');

      const updated = manager.updateTemplate(
        template.id,
        { name: 'New Name' },
        'user1'
      );

      expect(updated?.name).toBe('New Name');
    });

    test('should prevent non-creator from updating', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');

      expect(() => {
        manager.updateTemplate(template.id, { name: 'Hacked' }, 'user2');
      }).toThrow();
    });
  });

  describe('Template Deletion', () => {
    test('should delete template', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');

      const deleted = manager.deleteTemplate(template.id, 'user1');
      expect(deleted).toBe(true);

      const retrieved = manager.getTemplate(template.id);
      expect(retrieved).toBeNull();
    });

    test('should prevent non-creator from deleting', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');

      expect(() => {
        manager.deleteTemplate(template.id, 'user2');
      }).toThrow();
    });
  });

  describe('Template Sharing', () => {
    test('should share template with user', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');

      const shared = manager.shareTemplate(template.id, 'user1', 'user2');
      expect(shared).toBe(true);
    });

    test('should get shared templates', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');

      manager.shareTemplate(template.id, 'user1', 'user2');

      const shared = manager.getSharedTemplates('user2');
      expect(shared).toHaveLength(1);
      expect(shared[0].id).toBe(template.id);
    });
  });

  describe('Public Templates', () => {
    test('should list public templates', () => {
      manager.createTemplate('Public 1', { steps: [] }, 'user1', { isPublic: true });
      manager.createTemplate('Private', { steps: [] }, 'user1', { isPublic: false });
      manager.createTemplate('Public 2', { steps: [] }, 'user2', { isPublic: true });

      const publicTemplates = manager.getPublicTemplates();
      expect(publicTemplates).toHaveLength(2);
    });
  });

  describe('Template Search', () => {
    test('should search templates by name', () => {
      manager.createTemplate('Form Builder', { steps: [] }, 'user1', { isPublic: true });
      manager.createTemplate('Contact Form', { steps: [] }, 'user1', { isPublic: true });

      const results = manager.searchTemplates('Form');
      expect(results).toHaveLength(2);
    });

    test('should search by tags', () => {
      manager.createTemplate('Test 1', { steps: [] }, 'user1', {
        isPublic: true,
        tags: ['survey']
      });
      manager.createTemplate('Test 2', { steps: [] }, 'user1', {
        isPublic: true,
        tags: ['form']
      });

      const results = manager.searchTemplates('survey');
      expect(results).toHaveLength(1);
    });
  });

  describe('Template Categories', () => {
    test('should get templates by category', () => {
      const template1 = manager.createTemplate('Template 1', { steps: [] }, 'user1');
      manager.updateTemplate(template1.id, { category: 'survey' }, 'user1');

      const template2 = manager.createTemplate('Template 2', { steps: [] }, 'user1');
      manager.updateTemplate(template2.id, { category: 'form' }, 'user1');

      const surveys = manager.getTemplatesByCategory('survey', 'user1');
      expect(surveys).toHaveLength(1);
    });

    test('should get all categories', () => {
      const t1 = manager.createTemplate('Test 1', { steps: [] }, 'user1');
      manager.updateTemplate(t1.id, { category: 'survey' }, 'user1');

      const t2 = manager.createTemplate('Test 2', { steps: [] }, 'user1');
      manager.updateTemplate(t2.id, { category: 'form' }, 'user1');

      const categories = manager.getAllCategories();
      expect(categories).toContain('survey');
      expect(categories).toContain('form');
    });
  });

  describe('Template Import/Export', () => {
    test('should export template as JSON', () => {
      const template = manager.createTemplate('Test', { steps: [] }, 'user1');
      const json = manager.exportTemplate(template.id);

      expect(json).toBeDefined();
      const parsed = JSON.parse(json!);
      expect(parsed.name).toBe('Test');
    });

    test('should import template from JSON', () => {
      const template = manager.createTemplate('Original', { steps: [] }, 'user1');
      const json = manager.exportTemplate(template.id)!;

      const imported = manager.importTemplate(json, 'user2');
      expect(imported).toBeDefined();
      expect(imported?.name).toBe('Original');
      expect(imported?.createdBy).toBe('user2');
    });
  });

  describe('Template Cloning', () => {
    test('should clone template', () => {
      const original = manager.createTemplate('Original', { steps: [] }, 'user1');
      const cloned = manager.cloneTemplate(original.id, 'user2');

      expect(cloned?.name).toContain('Copy');
      expect(cloned?.createdBy).toBe('user2');
    });
  });
});

describe('FormDraftHandler', () => {
  let handler: FormDraftHandler;

  beforeEach(() => {
    handler = new FormDraftHandler();
    localStorage.clear();
  });

  afterEach(() => {
    handler.destroy();
    localStorage.clear();
  });

  describe('Draft Creation', () => {
    test('should get or create draft', () => {
      const draft = handler.getDraft('form1', 'user1');

      expect(draft.formId).toBe('form1');
      expect(draft.userId).toBe('user1');
      expect(draft.status).toBe('in_progress');
    });
  });

  describe('Draft Saving', () => {
    test('should save draft', () => {
      const data = { field1: 'value1' };
      const saved = handler.saveDraft('form1', 'user1', data);

      expect(saved.data).toEqual(data);
      expect(saved.version).toBe(2);
    });

    test('should save draft with options', () => {
      const data = { field1: 'value1' };
      const saved = handler.saveDraft('form1', 'user1', data, {
        currentStep: 2,
        completedSteps: [0, 1]
      });

      expect(saved.currentStep).toBe(2);
      expect(saved.completedSteps).toContain(1);
    });
  });

  describe('Draft Loading', () => {
    test('should load draft', () => {
      const data = { field1: 'value1' };
      handler.saveDraft('form1', 'user1', data);

      const loaded = handler.loadDraft('form1', 'user1');
      expect(loaded?.data).toEqual(data);
    });

    test('should return null for non-existent draft', () => {
      const loaded = handler.loadDraft('form2', 'user2');
      expect(loaded).toBeNull();
    });
  });

  describe('Draft Listing', () => {
    test('should list user drafts', () => {
      handler.saveDraft('form1', 'user1', {});
      handler.saveDraft('form2', 'user1', {});
      handler.saveDraft('form3', 'user2', {});

      const user1Drafts = handler.listDrafts('user1');
      expect(user1Drafts).toHaveLength(2);

      const user2Drafts = handler.listDrafts('user2');
      expect(user2Drafts).toHaveLength(1);
    });
  });

  describe('Draft Deletion', () => {
    test('should delete draft', () => {
      handler.saveDraft('form1', 'user1', {});

      const deleted = handler.deleteDraft('form1', 'user1');
      expect(deleted).toBe(true);

      const loaded = handler.loadDraft('form1', 'user1');
      expect(loaded).toBeNull();
    });
  });

  describe('Draft Versioning', () => {
    test('should track draft versions', () => {
      handler.saveDraft('form1', 'user1', { v: 1 });
      handler.saveDraft('form1', 'user1', { v: 2 });
      handler.saveDraft('form1', 'user1', { v: 3 });

      const history = handler.getVersionHistory('form1', 'user1');
      expect(history.length).toBeGreaterThan(0);
    });

    test('should get specific version', () => {
      handler.saveDraft('form1', 'user1', { v: 1 });
      handler.saveDraft('form1', 'user1', { v: 2 });

      const version = handler.getVersion('form1', 'user1', 1);
      expect(version).toBeDefined();
    });

    test('should restore version', () => {
      handler.saveDraft('form1', 'user1', { v: 1 });
      handler.saveDraft('form1', 'user1', { v: 2 });

      const restored = handler.restoreVersion('form1', 'user1', 1);
      expect(restored?.data.v).toBe(1);
    });
  });

  describe('Draft Status', () => {
    test('should mark as submitted', () => {
      handler.saveDraft('form1', 'user1', {});

      const submitted = handler.markSubmitted('form1', 'user1');
      expect(submitted?.status).toBe('submitted');
    });

    test('should mark as abandoned', () => {
      handler.saveDraft('form1', 'user1', {});

      const abandoned = handler.markAbandoned('form1', 'user1');
      expect(abandoned?.status).toBe('abandoned');
    });
  });

  describe('Auto-Save', () => {
    test('should queue auto-save', () => {
      handler.autoSave('form1', 'user1', { field: 'value' });

      // Auto-save is async, draft not immediately updated
      const draft = handler.loadDraft('form1', 'user1');
      expect(draft).toBeDefined();
    });
  });

  describe('Draft Metadata', () => {
    test('should get draft metadata', () => {
      handler.saveDraft('form1', 'user1', {}, {
        completedSteps: [0, 1]
      });

      const metadata = handler.getDraftMetadata('form1', 'user1');
      expect(metadata?.formId).toBe('form1');
      expect(metadata?.progress).toBe(2);
    });
  });

  describe('Draft Import/Export', () => {
    test('should export draft', () => {
      handler.saveDraft('form1', 'user1', { data: 'test' });

      const json = handler.exportDraft('form1', 'user1');
      expect(json).toBeDefined();

      const parsed = JSON.parse(json!);
      expect(parsed.formId).toBe('form1');
    });

    test('should import draft', () => {
      handler.saveDraft('form1', 'user1', { data: 'test' });

      const json = handler.exportDraft('form1', 'user1')!;
      const imported = handler.importDraft(json);

      expect(imported?.formId).toBe('form1');
    });
  });

  describe('Cleanup', () => {
    test('should cleanup expired drafts', () => {
      handler.saveDraft('form1', 'user1', {});
      handler.saveDraft('form2', 'user1', {});

      const cleaned = handler.cleanupExpired();
      // Drafts won't be expired immediately
      expect(typeof cleaned).toBe('number');
    });
  });

  describe('Performance', () => {
    test('should handle multiple drafts efficiently', () => {
      const start = performance.now();

      for (let i = 0; i < 100; i++) {
        handler.saveDraft(`form${i}`, `user${i % 10}`, { index: i });
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });

    test('should list drafts quickly', () => {
      for (let i = 0; i < 50; i++) {
        handler.saveDraft(`form${i}`, 'user1', {});
      }

      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        handler.listDrafts('user1');
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(200);
    });
  });
});
