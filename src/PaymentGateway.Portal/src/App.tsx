import { useState } from 'react';
import { AuthProvider } from './context/AuthContext';
import { AppShell } from './components/layout/AppShell';
import { UnresolvedConsole } from './components/unresolved/UnresolvedConsole';
import { WebhooksConsole } from './components/webhooks/WebhooksConsole';
import { RiskConsole } from './components/risk/RiskConsole';
import { ApiKeysConsole } from './components/apikeys/ApiKeysConsole';
import { WebhookDlqConsole } from './components/webhooks/WebhookDlqConsole';
import { ObservabilityConsole } from './components/observability/ObservabilityConsole';
import { ThreeDsSimulator } from './components/threeds/ThreeDsSimulator';

function PortalContent() {
  const [activeTab, setActiveTab] = useState<'unresolved' | 'webhooks' | 'risk' | 'api-keys' | 'webhook-dlq' | 'observability' | 'threeds'>('unresolved');

  return (
    <AppShell activeTab={activeTab} onTabChange={setActiveTab}>
      {activeTab === 'unresolved' && <UnresolvedConsole />}

      {activeTab === 'webhooks' && <WebhooksConsole />}

      {activeTab === 'risk' && <RiskConsole />}

      {activeTab === 'api-keys' && <ApiKeysConsole />}

      {activeTab === 'webhook-dlq' && <WebhookDlqConsole />}

      {activeTab === 'observability' && <ObservabilityConsole onNavigate={setActiveTab} />}

      {activeTab === 'threeds' && <ThreeDsSimulator />}
    </AppShell>
  );
}

export function App() {
  return (
    <AuthProvider>
      <PortalContent />
    </AuthProvider>
  );
}

export default App;
