/**
 * FormPermissionsTests - Test suite for role-based access control
 */

import { FormPermissions } from '../wwwroot/js/form-permissions';

describe('FormPermissions', () => {
  let permissions: FormPermissions;

  beforeEach(() => {
    permissions = new FormPermissions();
  });

  describe('Initialization', () => {
    test('should initialize with default permissions', () => {
      const viewPerm = permissions.getPermission('form:view');
      expect(viewPerm).toBeDefined();
      expect(viewPerm?.name).toBe('View Form');
    });

    test('should initialize with default roles', () => {
      const viewerRole = permissions.getRole('viewer');
      expect(viewerRole).toBeDefined();
      expect(viewerRole?.name).toBe('Viewer');
    });

    test('should have three default roles', () => {
      const roles = permissions.getAllRoles();
      expect(roles.length).toBe(3);
    });
  });

  describe('Permissions', () => {
    test('should add custom permission', () => {
      permissions.addPermission({
        id: 'custom:action',
        name: 'Custom Action',
        description: 'A custom action'
      });

      const perm = permissions.getPermission('custom:action');
      expect(perm?.name).toBe('Custom Action');
    });

    test('should remove permission', () => {
      permissions.removePermission('form:view');

      const perm = permissions.getPermission('form:view');
      expect(perm).toBeUndefined();
    });

    test('should support chainable API', () => {
      const result = permissions
        .addPermission({
          id: 'test:1',
          name: 'Test 1'
        })
        .addPermission({
          id: 'test:2',
          name: 'Test 2'
        });

      expect(result).toBe(permissions);
    });
  });

  describe('Roles', () => {
    test('should add custom role', () => {
      permissions.addRole({
        id: 'reviewer',
        name: 'Reviewer',
        permissions: ['form:view'],
        description: 'Can review forms'
      });

      const role = permissions.getRole('reviewer');
      expect(role?.name).toBe('Reviewer');
    });

    test('should remove role', () => {
      permissions.removeRole('viewer');

      const role = permissions.getRole('viewer');
      expect(role).toBeUndefined();
    });

    test('should get all roles', () => {
      const roles = permissions.getAllRoles();
      const roleIds = roles.map(r => r.id);

      expect(roleIds).toContain('viewer');
      expect(roleIds).toContain('editor');
      expect(roleIds).toContain('admin');
    });
  });

  describe('User Role Assignment', () => {
    test('should assign role to user', () => {
      permissions.assignRole('user1', 'editor');

      const roles = permissions.getUserRoles('user1');
      expect(roles).toContain('editor');
    });

    test('should assign multiple roles', () => {
      permissions
        .assignRole('user1', 'viewer')
        .assignRole('user1', 'editor');

      const roles = permissions.getUserRoles('user1');
      expect(roles).toContain('viewer');
      expect(roles).toContain('editor');
      expect(roles.length).toBe(2);
    });

    test('should not duplicate role assignment', () => {
      permissions.assignRole('user1', 'editor');
      permissions.assignRole('user1', 'editor');

      const roles = permissions.getUserRoles('user1');
      expect(roles.filter(r => r === 'editor').length).toBe(1);
    });

    test('should throw error for invalid role', () => {
      expect(() => {
        permissions.assignRole('user1', 'nonexistent');
      }).toThrow();
    });

    test('should remove role from user', () => {
      permissions.assignRole('user1', 'editor');
      permissions.removeRole('user1', 'editor');

      const roles = permissions.getUserRoles('user1');
      expect(roles).not.toContain('editor');
    });
  });

  describe('User Permissions', () => {
    test('should get user permissions from roles', () => {
      permissions.assignRole('user1', 'editor');

      const perms = permissions.getUserPermissions('user1');
      expect(perms).toContain('form:view');
      expect(perms).toContain('form:edit');
      expect(perms).toContain('form:submit');
    });

    test('should aggregate permissions from multiple roles', () => {
      permissions.assignRole('user1', 'viewer');
      permissions.assignRole('user1', 'editor');

      const perms = permissions.getUserPermissions('user1');
      // Should have all permissions from both roles (unique)
      expect(perms.length).toBeGreaterThanOrEqual(3);
    });

    test('should return empty permissions for user without roles', () => {
      const perms = permissions.getUserPermissions('unknown');
      expect(perms).toEqual([]);
    });
  });

  describe('Permission Checking', () => {
    test('should check if user has permission', () => {
      permissions.assignRole('user1', 'editor');

      expect(permissions.hasPermission('user1', 'form:edit')).toBe(true);
      expect(permissions.hasPermission('user1', 'form:admin')).toBe(false);
    });

    test('should check if user has role', () => {
      permissions.assignRole('user1', 'editor');

      expect(permissions.hasRole('user1', 'editor')).toBe(true);
      expect(permissions.hasRole('user1', 'admin')).toBe(false);
    });
  });

  describe('Form Access Control', () => {
    test('should allow access to all forms by default', () => {
      const canAccess = permissions.canAccessForm('user1', 'form1');
      expect(canAccess).toBe(true);
    });

    test('should restrict form access by role', () => {
      permissions.setFormAccess('form1', ['admin']);

      expect(permissions.canAccessForm('admin_user', 'form1')).toBe(true);
    });

    test('should check allowed users', () => {
      permissions.setFormAccess('form1', ['admin'], {
        allowedUsers: ['user1']
      });

      expect(permissions.canAccessForm('user1', 'form1')).toBe(true);
      expect(permissions.canAccessForm('user2', 'form1')).toBe(false);
    });

    test('should deny access to denied users', () => {
      permissions.setFormAccess('form1', ['admin'], {
        deniedUsers: ['user1']
      });

      expect(permissions.canAccessForm('user1', 'form1')).toBe(false);
    });

    test('should get accessible forms for user', () => {
      permissions.assignRole('user1', 'editor');
      permissions.setFormAccess('form1', ['editor']);
      permissions.setFormAccess('form2', ['admin']);

      const accessible = permissions.getAccessibleForms('user1', ['form1', 'form2']);
      expect(accessible).toContain('form1');
      expect(accessible).not.toContain('form2');
    });
  });

  describe('Field Access Control', () => {
    test('should allow access to all fields by default', () => {
      const canAccess = permissions.canAccessField('user1', 'field1');
      expect(canAccess).toBe(true);
    });

    test('should restrict field access by role', () => {
      permissions.setFieldAccess('field1', ['admin']);
      permissions.assignRole('user1', 'editor');

      expect(permissions.canAccessField('user1', 'field1')).toBe(false);
    });

    test('should get accessible fields for user', () => {
      permissions.assignRole('user1', 'editor');
      permissions.setFieldAccess('field1', ['editor']);
      permissions.setFieldAccess('field2', ['admin']);

      const accessible = permissions.getAccessibleFields('user1', ['field1', 'field2']);
      expect(accessible).toContain('field1');
      expect(accessible).not.toContain('field2');
    });
  });

  describe('Access Control Information', () => {
    test('should get access control for resource', () => {
      permissions.setFormAccess('form1', ['admin']);

      const access = permissions.getAccessControl('form1');
      expect(access).toBeDefined();
      expect(access?.allowedRoles).toContain('admin');
    });

    test('should return undefined for unrestricted resource', () => {
      const access = permissions.getAccessControl('unknown');
      expect(access).toBeUndefined();
    });
  });

  describe('Configuration Export', () => {
    test('should export configuration', () => {
      permissions.assignRole('user1', 'admin');
      permissions.setFormAccess('form1', ['admin']);

      const config = permissions.exportConfiguration();

      expect(config).toHaveProperty('permissions');
      expect(config).toHaveProperty('roles');
      expect(config).toHaveProperty('userRoles');
      expect(config).toHaveProperty('resourcePermissions');
    });

    test('should include user roles in export', () => {
      permissions.assignRole('user1', 'admin');

      const config = permissions.exportConfiguration();
      expect(config.userRoles['user1']).toContain('admin');
    });
  });

  describe('Reset', () => {
    test('should reset to defaults', () => {
      permissions.addRole({
        id: 'custom',
        name: 'Custom',
        permissions: []
      });

      permissions.reset();

      const customRole = permissions.getRole('custom');
      expect(customRole).toBeUndefined();

      const defaultRole = permissions.getRole('viewer');
      expect(defaultRole).toBeDefined();
    });
  });

  describe('Callbacks', () => {
    test('should set permission change callback', () => {
      const callback = jest.fn();
      permissions.onPermissionChanged(callback);

      expect(callback).not.toHaveBeenCalled();
    });
  });

  describe('Complex Scenarios', () => {
    test('should handle multiple roles with overlapping permissions', () => {
      permissions.addRole({
        id: 'approver',
        name: 'Approver',
        permissions: ['form:view', 'form:approve']
      });

      permissions.assignRole('user1', 'editor');
      permissions.assignRole('user1', 'approver');

      const perms = permissions.getUserPermissions('user1');
      expect(perms).toContain('form:view');
      expect(perms).toContain('form:edit');
      expect(perms).toContain('form:approve');
    });

    test('should handle mixed user and role-based access', () => {
      permissions.setFormAccess('form1', ['admin'], {
        allowedUsers: ['special_user'],
        deniedUsers: ['blocked_user']
      });

      expect(permissions.canAccessForm('special_user', 'form1')).toBe(true);
      expect(permissions.canAccessForm('blocked_user', 'form1')).toBe(false);

      permissions.assignRole('admin_user', 'admin');
      expect(permissions.canAccessForm('admin_user', 'form1')).toBe(true);
    });
  });

  describe('Performance', () => {
    test('should check permissions quickly', () => {
      permissions.assignRole('user1', 'admin');

      const start = performance.now();

      for (let i = 0; i < 10000; i++) {
        permissions.hasPermission('user1', 'form:edit');
        permissions.hasRole('user1', 'admin');
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(100);
    });

    test('should handle many users efficiently', () => {
      const start = performance.now();

      for (let i = 0; i < 1000; i++) {
        permissions.assignRole(`user${i}`, 'editor');
      }

      const elapsed = performance.now() - start;
      expect(elapsed).toBeLessThan(500);
    });
  });
});
