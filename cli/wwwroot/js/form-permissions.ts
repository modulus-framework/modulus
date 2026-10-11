/**
 * FormPermissions - Role-based access control for forms and fields
 * Manages permissions and authorization rules
 */

export interface Permission {
  id: string;
  name: string;
  description?: string;
  resource?: string;
}

export interface Role {
  id: string;
  name: string;
  permissions: string[];
  description?: string;
}

export interface ResourcePermission {
  resourceId: string;
  resourceType: 'form' | 'field' | 'step';
  allowedRoles: string[];
  allowedUsers?: string[];
  deniedUsers?: string[];
}

export interface AccessControl {
  userId: string;
  userRoles: string[];
  resources: ResourcePermission[];
}

export class FormPermissions {
  private permissions: Map<string, Permission>;
  private roles: Map<string, Role>;
  private userRoles: Map<string, string[]>;
  private resourcePermissions: Map<string, ResourcePermission>;
  private onPermissionChanged: ((role: string) => void) | null = null;

  constructor() {
    this.permissions = new Map();
    this.roles = new Map();
    this.userRoles = new Map();
    this.resourcePermissions = new Map();
    this.initializeDefaultPermissions();
  }

  /**
   * Initialize default permissions and roles
   */
  private initializeDefaultPermissions(): void {
    // Default permissions
    this.addPermission({
      id: 'form:view',
      name: 'View Form',
      description: 'Can view form'
    });

    this.addPermission({
      id: 'form:edit',
      name: 'Edit Form',
      description: 'Can edit form data'
    });

    this.addPermission({
      id: 'form:submit',
      name: 'Submit Form',
      description: 'Can submit form'
    });

    this.addPermission({
      id: 'form:admin',
      name: 'Admin Form',
      description: 'Can manage form'
    });

    // Default roles
    this.addRole({
      id: 'viewer',
      name: 'Viewer',
      permissions: ['form:view'],
      description: 'Can view forms only'
    });

    this.addRole({
      id: 'editor',
      name: 'Editor',
      permissions: ['form:view', 'form:edit', 'form:submit'],
      description: 'Can view and edit forms'
    });

    this.addRole({
      id: 'admin',
      name: 'Administrator',
      permissions: ['form:view', 'form:edit', 'form:submit', 'form:admin'],
      description: 'Full form management'
    });
  }

  /**
   * Add a permission
   */
  addPermission(permission: Permission): FormPermissions {
    this.permissions.set(permission.id, permission);
    return this;
  }

  /**
   * Remove a permission
   */
  removePermission(permissionId: string): FormPermissions {
    this.permissions.delete(permissionId);
    return this;
  }

  /**
   * Get permission
   */
  getPermission(permissionId: string): Permission | undefined {
    return this.permissions.get(permissionId);
  }

  /**
   * Add a role
   */
  addRole(role: Role): FormPermissions {
    this.roles.set(role.id, role);
    return this;
  }

  /**
   * Remove a role
   */
  removeRole(roleId: string): FormPermissions {
    this.roles.delete(roleId);
    this.userRoles.forEach((roles) => {
      const index = roles.indexOf(roleId);
      if (index > -1) {
        roles.splice(index, 1);
      }
    });
    return this;
  }

  /**
   * Get role
   */
  getRole(roleId: string): Role | undefined {
    return this.roles.get(roleId);
  }

  /**
   * Get all roles
   */
  getAllRoles(): Role[] {
    return Array.from(this.roles.values());
  }

  /**
   * Assign role to user
   */
  assignRole(userId: string, roleId: string): FormPermissions {
    if (!this.roles.has(roleId)) {
      throw new Error(`Role not found: ${roleId}`);
    }

    if (!this.userRoles.has(userId)) {
      this.userRoles.set(userId, []);
    }

    const userRoles = this.userRoles.get(userId)!;
    if (!userRoles.includes(roleId)) {
      userRoles.push(roleId);
    }

    if (this.onPermissionChanged) {
      this.onPermissionChanged(roleId);
    }

    return this;
  }

  /**
   * Remove role from user
   */
  removeRole(userId: string, roleId: string): FormPermissions {
    const userRoles = this.userRoles.get(userId);
    if (userRoles) {
      const index = userRoles.indexOf(roleId);
      if (index > -1) {
        userRoles.splice(index, 1);
      }
    }

    if (this.onPermissionChanged) {
      this.onPermissionChanged(roleId);
    }

    return this;
  }

  /**
   * Get user's roles
   */
  getUserRoles(userId: string): string[] {
    return [...(this.userRoles.get(userId) || [])];
  }

  /**
   * Get user's permissions
   */
  getUserPermissions(userId: string): string[] {
    const userRoles = this.userRoles.get(userId) || [];
    const permissions = new Set<string>();

    for (const roleId of userRoles) {
      const role = this.roles.get(roleId);
      if (role) {
        role.permissions.forEach(p => permissions.add(p));
      }
    }

    return Array.from(permissions);
  }

  /**
   * Check if user has permission
   */
  hasPermission(userId: string, permissionId: string): boolean {
    return this.getUserPermissions(userId).includes(permissionId);
  }

  /**
   * Check if user has any role
   */
  hasRole(userId: string, roleId: string): boolean {
    return this.getUserRoles(userId).includes(roleId);
  }

  /**
   * Set form-level access control
   */
  setFormAccess(
    formId: string,
    allowedRoles: string[],
    options?: { allowedUsers?: string[]; deniedUsers?: string[] }
  ): FormPermissions {
    this.resourcePermissions.set(formId, {
      resourceId: formId,
      resourceType: 'form',
      allowedRoles,
      allowedUsers: options?.allowedUsers,
      deniedUsers: options?.deniedUsers
    });

    return this;
  }

  /**
   * Set field-level access control
   */
  setFieldAccess(
    fieldId: string,
    allowedRoles: string[],
    options?: { allowedUsers?: string[]; deniedUsers?: string[] }
  ): FormPermissions {
    this.resourcePermissions.set(fieldId, {
      resourceId: fieldId,
      resourceType: 'field',
      allowedRoles,
      allowedUsers: options?.allowedUsers,
      deniedUsers: options?.deniedUsers
    });

    return this;
  }

  /**
   * Check if user can access form
   */
  canAccessForm(userId: string, formId: string): boolean {
    const access = this.resourcePermissions.get(formId);

    if (!access) {
      return true; // No restrictions
    }

    // Check denied users first
    if (access.deniedUsers?.includes(userId)) {
      return false;
    }

    // Check allowed users
    if (access.allowedUsers?.includes(userId)) {
      return true;
    }

    // Check user's roles
    const userRoles = this.getUserRoles(userId);
    return access.allowedRoles.some(role => userRoles.includes(role));
  }

  /**
   * Check if user can access field
   */
  canAccessField(userId: string, fieldId: string): boolean {
    const access = this.resourcePermissions.get(fieldId);

    if (!access) {
      return true; // No restrictions
    }

    // Check denied users first
    if (access.deniedUsers?.includes(userId)) {
      return false;
    }

    // Check allowed users
    if (access.allowedUsers?.includes(userId)) {
      return true;
    }

    // Check user's roles
    const userRoles = this.getUserRoles(userId);
    return access.allowedRoles.some(role => userRoles.includes(role));
  }

  /**
   * Get accessible forms for user
   */
  getAccessibleForms(userId: string, allFormIds: string[]): string[] {
    return allFormIds.filter(formId => this.canAccessForm(userId, formId));
  }

  /**
   * Get accessible fields for user
   */
  getAccessibleFields(userId: string, fieldIds: string[]): string[] {
    return fieldIds.filter(fieldId => this.canAccessField(userId, fieldId));
  }

  /**
   * Get access control for resource
   */
  getAccessControl(resourceId: string): ResourcePermission | undefined {
    return this.resourcePermissions.get(resourceId);
  }

  /**
   * Set permission change callback
   */
  onPermissionChanged(callback: (role: string) => void): FormPermissions {
    this.onPermissionChanged = callback;
    return this;
  }

  /**
   * Export permissions and roles
   */
  exportConfiguration(): {
    permissions: Permission[];
    roles: Role[];
    userRoles: Record<string, string[]>;
    resourcePermissions: Record<string, ResourcePermission>;
  } {
    return {
      permissions: Array.from(this.permissions.values()),
      roles: Array.from(this.roles.values()),
      userRoles: Object.fromEntries(this.userRoles),
      resourcePermissions: Object.fromEntries(this.resourcePermissions)
    };
  }

  /**
   * Reset to default permissions
   */
  reset(): FormPermissions {
    this.permissions.clear();
    this.roles.clear();
    this.userRoles.clear();
    this.resourcePermissions.clear();
    this.initializeDefaultPermissions();
    return this;
  }
}
