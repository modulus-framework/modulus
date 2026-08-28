import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className="hero__title">
          {siteConfig.title}
        </Heading>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link
            className="button button--secondary button--lg"
            to="/docs/introduction">
            Get Started
          </Link>
          <Link
            className="button button--secondary button--lg"
            to="/docs/cli/"
            style={{marginLeft: '1rem'}}>
            CLI Reference
          </Link>
        </div>
      </div>
    </header>
  );
}

function FeatureSection() {
  const features = [
    {
      title: 'Modular Architecture',
      description: 'Clean boundaries between business domains with independent databases, handlers, and API surfaces.',
    },
    {
      title: 'CQRS & Events',
      description: 'Built-in mediator with pipeline behaviors, transactional outbox, and inbox deduplication.',
    },
    {
      title: 'Production Ready',
      description: 'Rate limiting, health checks, idempotency, security headers, PII encryption, and feature flags out of the box.',
    },
    {
      title: 'CLI Scaffolding',
      description: 'Generate complete applications, modules, and CRUD operations with interactive setup.',
    },
    {
      title: 'Multi-Tenant',
      description: 'Per-tenant data isolation with automatic query filters across relational and NoSQL databases.',
    },
    {
      title: 'Identity & Auth',
      description: 'OpenIddict server with 6 external IdP adapters (Auth0, Keycloak, Azure AD, Okta, etc.).',
    },
  ];

  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {features.map((feature, idx) => (
            <div key={idx} className={clsx('col col--4')}>
              <div className="text--center padding-horiz--md padding-vert--lg">
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

function CodeExample() {
  return (
    <section className={styles.codeSection}>
      <div className="container">
        <Heading as="h2" className="text--center" style={{marginBottom: '2rem'}}>
          Build your first app in 3 commands
        </Heading>
        <div className={styles.codeBlock}>
          <pre><code>{`# Install the CLI
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli

# Create an application
modulus app MyApp

# Add a module with CRUD
modulus add-module Catalog
modulus generate-crud Product --module Catalog

# Run
cd src/API/MyApp.Api
dotnet run`}</code></pre>
        </div>
      </div>
    </section>
  );
}

export default function Home(): JSX.Element {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title="Home"
      description={siteConfig.tagline}>
      <HomepageHeader />
      <main>
        <FeatureSection />
        <CodeExample />
      </main>
    </Layout>
  );
}
