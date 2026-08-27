/**
 * Food Delivery API - Swagger UI Customizations
 * Enhances Swagger UI with professional features and custom functionality
 */

(function() {
    'use strict';

    // Wait for DOM to be ready
    document.addEventListener('DOMContentLoaded', function() {
        initializeSwaggerCustomizations();
    });

    /**
     * Initialize all custom features
     */
    function initializeSwaggerCustomizations() {
        // Add request duration display for all operations
        addRequestDurationDisplay();

        // Add copy to clipboard buttons
        addCopyButtons();

        // Add expand/collapse all button
        addExpandCollapseAllButton();

        // Add server environment indicator
        addServerEnvironmentBadge();

        // Enhance tags with module categories
        enhanceTagDisplay();

        // Add API version banner
        addVersionBanner();

        // Add search functionality
        enhanceSearchFunctionality();

        // Add webhook documentation link
        addWebhookDocumentationLink();

        // Initialize observers for dynamic content
        initializeMutationObservers();
    }

    /**
     * Add request duration display to each operation
     */
    function addRequestDurationDisplay() {
        const observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.addedNodes.length > 0) {
                    addDurationToOperations();
                }
            });
        });

        observer.observe(document.querySelector('.opblock-tag-section'), {
            childList: true,
            subtree: true
        });
    }

    /**
     * Add duration badge to operations
     */
    function addDurationToOperations() {
        const operations = document.querySelectorAll('.opblock');
        operations.forEach(function(opblock) {
            const summary = opblock.querySelector('.opblock-summary');
            if (summary && !summary.querySelector('.duration-badge')) {
                const badge = document.createElement('span');
                badge.className = 'duration-badge';
                badge.innerHTML = '< 200ms';
                badge.title = 'Expected response time';
                badge.style.cssText = `
                    display: inline-flex;
                    align-items: center;
                    margin-left: auto;
                    padding: 4px 10px;
                    background: linear-gradient(135deg, #10b981 0%, #0891b2 100%);
                    color: white;
                    border-radius: 12px;
                    font-size: 11px;
                    font-weight: 600;
                    box-shadow: 0 1px 3px rgba(16, 185, 129, 0.15);
                `;

                const methodElement = summary.querySelector('.method');
                if (methodElement) {
                    methodElement.style.flexDirection = 'row';
                    methodElement.style.justifyContent = 'space-between';
                    methodElement.appendChild(badge);
                }
            }
        });
    }

    /**
     * Add copy button to code blocks and response examples
     */
    function addCopyButtons() {
        const observer = new MutationObserver(function(mutations) {
            const codeBlocks = document.querySelectorAll('pre code, .response-col_description code, .example__header code');
            codeBlocks.forEach(function(block) {
                if (!block.querySelector('.copy-btn')) {
                    const button = document.createElement('button');
                    button.className = 'copy-btn';
                    button.innerHTML = '📋 Copy';
                    button.title = 'Copy to clipboard';
                    button.onclick = function() {
                        copyToClipboard(block);
                    };
                    block.style.position = 'relative';
                    block.parentElement.style.position = 'relative';
                    block.parentElement.appendChild(button);
                }
            });
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    /**
     * Copy content to clipboard
     */
    function copyToClipboard(element) {
        const text = element.textContent || element.innerText;
        navigator.clipboard.writeText(text).then(function() {
            showToast('Copied to clipboard!', 'success');
        }).catch(function() {
            showToast('Failed to copy', 'error');
        });
    }

    /**
     * Show toast notification
     */
    function showToast(message, type) {
        const toast = document.createElement('div');
        toast.className = 'swagger-toast ' + type;
        toast.innerHTML = message;
        toast.style.cssText = `
            position: fixed;
            top: 20px;
            right: 20px;
            padding: 12px 20px;
            border-radius: 8px;
            color: white;
            font-weight: 600;
            z-index: 10000;
            animation: slideIn 0.3s ease;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        `;

        if (type === 'success') {
            toast.style.background = '#10b981';
        } else if (type === 'error') {
            toast.style.background = '#dc3545';
        } else {
            toast.style.background = '#3b82f6';
        }

        document.body.appendChild(toast);

        setTimeout(function() {
            toast.style.animation = 'slideOut 0.3s ease';
            setTimeout(function() {
                toast.remove();
            }, 300);
        }, 3000);
    }

    /**
     * Add expand/collapse all button
     */
    function addExpandCollapseAllButton() {
        const topbar = document.querySelector('.topbar-wrapper');
        if (topbar && !topbar.querySelector('.expand-collapse-all')) {
            const button = document.createElement('button');
            button.className = 'expand-collapse-all';
            button.innerHTML = '⬇ Expand All';
            button.title = 'Expand all operations';
            button.onclick = function() {
                const isExpanded = button.classList.contains('collapse');
                const sections = document.querySelectorAll('.opblock-tag');

                sections.forEach(function(section) {
                    const opblock = section.querySelector('.opblock');
                    if (isExpanded) {
                        opblock.classList.remove('expanded');
                    } else {
                        opblock.classList.add('expanded');
                    }
                });

                button.classList.toggle('collapse');
                button.innerHTML = isExpanded ? '⬇ Expand All' : '⬆ Collapse All';
            };

            topbar.appendChild(button);
        }
    }

    /**
     * Add server environment badge
     */
    function addServerEnvironmentBadge() {
        const serversSelect = document.querySelector('.servers select');
        if (serversSelect) {
            serversSelect.addEventListener('change', function() {
                const selectedServer = serversSelect.value;
                const badge = document.getElementById('env-badge');
                if (badge) {
                    updateEnvironmentBadge(badge, selectedServer);
                }
            });

            // Initial badge
            const badge = document.createElement('span');
            badge.id = 'env-badge';
            badge.className = 'env-badge';

            const wrapper = document.querySelector('.servers');
            if (wrapper) {
                wrapper.insertBefore(badge, wrapper);
            }
        }
    }

    /**
     * Update environment badge based on selected server
     */
    function updateEnvironmentBadge(badge, serverUrl) {
        if (serverUrl.includes('localhost') || serverUrl.includes('127.0.0.1')) {
            badge.textContent = 'Development';
            badge.className = 'env-badge env-dev';
        } else if (serverUrl.includes('staging') || serverUrl.includes('stg')) {
            badge.textContent = 'Staging';
            badge.className = 'env-badge env-staging';
        } else if (serverUrl.includes('api.')) {
            badge.textContent = 'Production';
            badge.className = 'env-badge env-production';
        } else {
            badge.textContent = serverUrl;
            badge.className = 'env-badge';
        }
    }

    /**
     * Enhance tag display with category icons
     */
    function enhanceTagDisplay() {
        const tagHeaders = document.querySelectorAll('.opblock-tag a');
        tagHeaders.forEach(function(header) {
            const tagLink = header.getAttribute('href');
            if (tagLink) {
                const module = tagLink.split('/').pop();
                addModuleInfoTooltip(header, module);
            }
        });
    }

    /**
     * Add module information tooltip
     */
    function addModuleInfoTooltip(element, moduleName) {
        const moduleInfo = {
            'users': { name: 'User Management', description: 'Authentication, profiles, and user management' },
            'catalog': { name: 'Catalog', description: 'Food items, categories, and menus' },
            'basket': { name: 'Basket', description: 'Shopping cart and saved items' },
            'orders': { name: 'Orders', description: 'Order management and tracking' },
            'payment': { name: 'Payment', description: 'Payment processing and refunds' },
            'delivery': { name: 'Delivery', description: 'Delivery tracking and management' },
            'cooks': { name: 'Cooks', description: 'Cook profiles and menu management' },
            'notifications': { name: 'Notifications', description: 'Real-time notifications (SignalR)' },
            'reviews': { name: 'Reviews', description: 'Customer reviews and ratings' },
            'analytics': { name: 'Analytics', description: 'Reports and metrics' },
            'promotions': { name: 'Promotions', description: 'Discount codes and campaigns' },
            'admin': { name: 'Admin', description: 'Platform administration' }
        };

        const info = moduleInfo[moduleName.toLowerCase()];
        if (info) {
            element.title = `${info.name} - ${info.description}`;
        }
    }

    /**
     * Add API version banner
     */
    function addVersionBanner() {
        const info = document.querySelector('.info');
        if (info) {
            const banner = document.createElement('div');
            banner.className = 'version-banner';
            banner.innerHTML = `
                <div class="version-info">
                    <strong>API Version 1.0</strong>
                    <span class="version-status">Stable</span>
                    <span class="version-lifecycle">Production Ready</span>
                </div>
            `;

            info.insertBefore(banner, info.firstChild);
        }
    }

    /**
     * Enhance search functionality
     */
    function enhanceSearchFunctionality() {
        // Add keyboard shortcut
        document.addEventListener('keydown', function(e) {
            // Press '/' to focus search
            if (e.key === '/' && e.target.tagName !== 'INPUT' && e.target.tagName !== 'TEXTAREA') {
                e.preventDefault();
                const searchInput = document.querySelector('.searchbox input');
                if (searchInput) {
                    searchInput.focus();
                }
            }
        });

        // Add search placeholder
        const searchInput = document.querySelector('.searchbox input');
        if (searchInput) {
            searchInput.placeholder = '🔍 Search endpoints, tags, schemas... (Press / to focus)';
        }
    }

    /**
     * Add webhook documentation link
     */
    function addWebhookDocumentationLink() {
        const info = document.querySelector('.info');
        if (info && !info.querySelector('.webhook-docs-link')) {
            const link = document.createElement('a');
            link.className = 'webhook-docs-link';
            link.href = 'https://docs.fooddelivery.com/webhooks';
            link.target = '_blank';
            link.innerHTML = '🔗 Webhook Documentation';
            link.title = 'View SignalR and webhook event documentation';

            const baseUrls = info.querySelector('.base-url');
            if (baseUrls) {
                baseUrls.parentElement.insertBefore(link, baseUrls.nextSibling);
            }
        }
    }

    /**
     * Initialize all mutation observers
     */
    function initializeMutationObservers() {
        // Observer for dynamically added operations
        const opblockObserver = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.addedNodes.length > 0) {
                    mutation.addedNodes.forEach(function(node) {
                        if (node.nodeType === 1) { // Element node
                            const opblocks = node.querySelectorAll('.opblock');
                            opblocks.forEach(function(opblock) {
                                addDurationBadge(opblock);
                            });
                        }
                    });
                }
            });
        });

        const targetNode = document.querySelector('.opblock-tag-section');
        if (targetNode) {
            opblockObserver.observe(targetNode, {
                childList: true,
                subtree: true
            });
        }
    }

    /**
     * Add duration badge to single operation
     */
    function addDurationBadge(opblock) {
        const summary = opblock.querySelector('.opblock-summary');
        if (summary && !summary.querySelector('.duration-badge')) {
            const badge = document.createElement('span');
            badge.className = 'duration-badge';
            badge.innerHTML = '< 200ms';
            badge.title = 'Expected response time';
            badge.style.cssText = `
                display: inline-flex;
                align-items: center;
                margin-left: auto;
                padding: 4px 10px;
                background: linear-gradient(135deg, #10b981 0%, #0891b2 100%);
                color: white;
                border-radius: 12px;
                font-size: 11px;
                font-weight: 600;
                box-shadow: 0 1px 3px rgba(16, 185, 129, 0.15);
            `;

            const methodElement = summary.querySelector('.method');
            if (methodElement) {
                methodElement.style.flexDirection = 'row';
                methodElement.style.justifyContent = 'space-between';
                methodElement.appendChild(badge);
            }
        }
    }

    /**
     * Apply CSS styles via JavaScript (for dynamic elements)
     */
    const style = document.createElement('style');
    style.textContent = `
        /* Toast animations */
        @keyframes slideIn {
            from {
                transform: translateX(400px);
                opacity: 0;
            }
            to {
                transform: translateX(0);
                opacity: 1;
            }
        }

        @keyframes slideOut {
            from {
                transform: translateX(0);
                opacity: 1;
            }
            to {
                transform: translateX(400px);
                opacity: 0;
            }
        }

        /* Copy button */
        .copy-btn {
            position: absolute;
            top: 8px;
            right: 8px;
            background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
            color: white;
            border: none;
            padding: 6px 12px;
            border-radius: 6px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
        }

        .copy-btn:hover {
            transform: scale(1.05);
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
        }

        .copy-btn:active {
            transform: scale(0.95);
        }

        /* Expand/Collapse All button */
        .expand-collapse-all {
            background: rgba(255, 255, 255, 0.95);
            border: 1px solid var(--primary-color);
            color: var(--primary-color);
            padding: 8px 16px;
            border-radius: 6px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            margin-left: auto;
        }

        .expand-collapse-all:hover {
            background: #fff;
            transform: translateY(-1px);
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
        }

        /* Environment badge */
        .env-badge {
            display: inline-block;
            padding: 4px 12px;
            border-radius: 6px;
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-left: 12px;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
        }

        .env-badge.env-dev {
            background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
            color: #fff;
        }

        .env-badge.env-staging {
            background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
            color: #fff;
        }

        .env-badge.env-production {
            background: linear-gradient(135deg, #dc3545 0%, #b91c1c 100%);
            color: #fff;
        }

        /* Version banner */
        .version-banner {
            background: linear-gradient(135deg, #FF5733 0%, #E64622 100%);
            padding: 12px 16px;
            border-radius: 8px;
            margin-bottom: 16px;
            box-shadow: 0 2px 4px rgba(255, 87, 51, 0.1);
        }

        .version-info {
            display: flex;
            align-items: center;
            gap: 12px;
            color: #fff;
            font-size: 13px;
        }

        .version-status {
            background: rgba(255, 255, 255, 0.2);
            padding: 2px 10px;
            border-radius: 4px;
            font-size: 11px;
        }

        .version-lifecycle {
            opacity: 0.8;
            font-size: 12px;
        }

        /* Webhook docs link */
        .webhook-docs-link {
            display: inline-flex;
            align-items: center;
            gap: 8px;
            padding: 8px 16px;
            background: var(--bg-light);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            text-decoration: none;
            color: var(--primary-color);
            font-weight: 600;
            font-size: 13px;
            transition: all 0.2s ease;
            margin-left: 16px;
        }

        .webhook-docs-link:hover {
            background: var(--primary-color);
            color: #fff;
            transform: translateY(-2px);
            box-shadow: 0 2px 4px rgba(255, 87, 51, 0.1);
        }

        /* Toast notifications */
        .swagger-toast {
            animation: slideIn 0.3s ease forwards;
        }

        .swagger-toast.success {
            background: linear-gradient(135deg, #10b981 0%, #0891b2 100%);
        }

        .swagger-toast.error {
            background: linear-gradient(135deg, #dc3545 0%, #b91c1c 100%);
        }

        .swagger-toast.info {
            background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
        }
    `;

    document.head.appendChild(style);
})();
