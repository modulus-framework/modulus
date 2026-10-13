import clsx from 'clsx';
import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

function Icon({children}: {children: ReactNode}) {
  return (
    <div className={styles.featureIcon}>
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true">
        {children}
      </svg>
    </div>
  );
}

const features = [
  {
    title: 'Modular Architecture',
    description:
      'Clean boundaries between business domains with independent databases, handlers, and API surfaces.',
    icon: (
      <>
        <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
        <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
        <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
        <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
      </>
    ),
  },
  {
    title: 'CQRS & Events',
    description:
      'Built-in mediator with pipeline behaviors, transactional outbox, and inbox deduplication.',
    icon: (
      <>
        <path d="M4 7h13l-3-3" />
        <path d="M20 17H7l3 3" />
      </>
    ),
  },
  {
    title: 'Production Ready',
    description:
      'Rate limiting, health checks, idempotency, security headers, PII encryption, and feature flags out of the box.',
    icon: (
      <>
        <path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6l7-3z" />
        <path d="M9 12l2 2 4-4" />
      </>
    ),
  },
  {
    title: 'CLI Scaffolding',
    description:
      'Generate complete applications, modules, and CRUD operations with interactive setup.',
    icon: (
      <>
        <rect x="3" y="4" width="18" height="16" rx="2" />
        <path d="M7 9l3 3-3 3" />
        <path d="M12 15h5" />
      </>
    ),
  },
  {
    title: 'Multi-Tenant',
    description:
      'Per-tenant data isolation with automatic query filters across relational and NoSQL databases.',
    icon: (
      <>
        <path d="M12 3l9 4.5-9 4.5-9-4.5L12 3z" />
        <path d="M3 12l9 4.5 9-4.5" />
        <path d="M3 16.5L12 21l9-4.5" />
      </>
    ),
  },
  {
    title: 'Identity & Auth',
    description:
      'OpenIddict server with 6 external IdP adapters (Auth0, Keycloak, Azure AD, Okta, etc.).',
    icon: (
      <>
        <rect x="4" y="10" width="16" height="10" rx="2" />
        <path d="M8 10V7a4 4 0 018 0v3" />
        <circle cx="12" cy="15" r="1.4" />
      </>
    ),
  },
];

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={styles.hero}>
      <div className={styles.heroGlowA} aria-hidden="true" />
      <div className={styles.heroGlowB} aria-hidden="true" />
      <div className="container">
        <img
          src="/img/logo.svg"
          alt="Modulus Logo"
          className={styles.heroLogo}
          width="104"
          height="104"
        />
        <Heading as="h1" className={styles.heroTitle}>
          {siteConfig.title}
        </Heading>
        <p className={styles.heroSubtitle}>{siteConfig.tagline}</p>
        <div className={styles.badges} aria-label="Highlights">
          <span className={styles.badge}>.NET 8</span>
          <span className={styles.badge}>.NET 10</span>
          <span className={styles.badge}>45 libraries</span>
          <span className={styles.badge}>Apache 2.0</span>
        </div>
        <div className={styles.buttons}>
          <Link
            className={clsx('button button--lg', styles.buttonPrimary)}
            to="/docs/introduction">
            Get Started
          </Link>
          <Link
            className={clsx('button button--lg', styles.buttonGhost)}
            to="/docs/cli/">
            CLI Reference
          </Link>
        </div>
      </div>
    </header>
  );
}

function FeatureSection() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className={styles.sectionHeading}>
          <Heading as="h2">Everything a modular monolith needs</Heading>
          <p>
            Batteries included, boundaries enforced — assemble an application
            from independent modules without losing developer ergonomics.
          </p>
        </div>
        <div className="row">
          {features.map((feature, idx) => (
            <div key={idx} className={clsx('col col--4', styles.featureCol)}>
              <div className={styles.featureCard}>
                <Icon>{feature.icon}</Icon>
                <Heading as="h3">{feature.title}</Heading>
                <p>{feature.description}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

type Line = {type: 'comment' | 'command'; text: string};

const terminalLines: Line[] = [
  {type: 'comment', text: '# Install the CLI'},
  {type: 'command', text: 'dotnet tool install -g --add-source ./nupkg Synvro.Modulus.Cli'},
  {type: 'comment', text: ''},
  {type: 'comment', text: '# Create an application'},
  {type: 'command', text: 'modulus app MyApp'},
  {type: 'comment', text: ''},
  {type: 'comment', text: '# Add a module with CRUD'},
  {type: 'command', text: 'modulus add-module Catalog'},
  {type: 'command', text: 'modulus generate-crud Product --module Catalog'},
  {type: 'comment', text: ''},
  {type: 'comment', text: '# Run'},
  {type: 'command', text: 'cd src/API/MyApp.Api'},
  {type: 'command', text: 'dotnet run'},
];

function CodeExample() {
  return (
    <section className={styles.codeSection}>
      <div className="container">
        <div className={styles.sectionHeading}>
          <Heading as="h2">From zero to a running app in minutes</Heading>
          <p>Install the CLI, scaffold an application, add a module with CRUD endpoints, and run it. Modules, databases and endpoints are wired for you.</p>
        </div>
        <div className={styles.terminal}>
          <div className={styles.terminalBar}>
            <span className={clsx(styles.dot, styles.dotRed)} />
            <span className={clsx(styles.dot, styles.dotYellow)} />
            <span className={clsx(styles.dot, styles.dotGreen)} />
            <span className={styles.terminalTitle}>terminal</span>
          </div>
          <pre className={styles.terminalBody}>
            <code>
              {terminalLines.map((line, idx) => (
                <div key={idx} className={line.type === 'comment' ? styles.lineComment : styles.lineCommand}>
                  {line.type === 'command' ? <span className={styles.prompt}>{'$ '}</span> : null}
                  {line.text}
                </div>
              ))}
            </code>
          </pre>
        </div>
      </div>
    </section>
  );
}

const stats = [
  {value: '45', label: 'framework libraries'},
  {value: '.NET 8 + 10', label: 'every package, both targets'},
  {value: '6', label: 'external identity providers'},
  {value: '4', label: 'layers per module (Domain to Presentation)'},
];

function StatsStrip() {
  return (
    <section className={styles.stats}>
      <div className="container">
        <div className={styles.statsGrid}>
          {stats.map((stat) => (
            <div key={stat.label} className={styles.stat}>
              <div className={styles.statValue}>{stat.value}</div>
              <div className={styles.statLabel}>{stat.label}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function ClosingCta() {
  return (
    <section className={styles.cta}>
      <div className="container">
        <Heading as="h2" className={styles.ctaTitle}>
          Ready to build a modular monolith?
        </Heading>
        <p className={styles.ctaText}>
          Read the getting-started guide, check compatibility with your .NET version, or browse the API reference.
        </p>
        <div className={styles.buttons}>
          <Link className={clsx('button button--lg', styles.buttonPrimary)} to="/docs/introduction">
            Read the docs
          </Link>
          <Link className={clsx('button button--lg', styles.buttonGhost)} to="/docs/compatibility">
            Compatibility
          </Link>
        </div>
      </div>
    </section>
  );
}

export default function Home(): JSX.Element {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout description={siteConfig.tagline}>
      <HomepageHeader />
      <main>
        <StatsStrip />
        <FeatureSection />
        <CodeExample />
        <ClosingCta />
      </main>
    </Layout>
  );
}
