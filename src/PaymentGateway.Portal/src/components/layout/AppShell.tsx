import React from 'react';
import { Header } from './Header';
import { AlertCircle, Radio, ShieldCheck, Activity, Key, AlertTriangle } from 'lucide-react';

interface AppShellProps {
  children: React.ReactNode;
  activeTab: 'unresolved' | 'webhooks' | 'risk' | 'api-keys' | 'webhook-dlq' | 'observability';
  onTabChange: (tab: 'unresolved' | 'webhooks' | 'risk' | 'api-keys' | 'webhook-dlq' | 'observability') => void;
}

export const AppShell: React.FC<AppShellProps> = ({ children, activeTab, onTabChange }) => {
  return (
    <div className="min-h-screen flex flex-col bg-[#0b0f19] text-[#f9fafb]">
      <Header />

      <div className="flex-1 flex flex-col md:flex-row">
        {/* Navigation Rail */}
        <aside className="w-full md:w-64 bg-[#111827] border-b md:border-b-0 md:border-r border-[#374151] p-4 flex flex-col shrink-0">
          <div className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 px-3">
            Operations Consoles
          </div>
          <nav className="flex flex-col gap-1">
            <button
              onClick={() => onTabChange('unresolved')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'unresolved'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>Unresolved Payments</span>
            </button>

            <button
              onClick={() => onTabChange('webhooks')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'webhooks'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <Radio className="w-4 h-4 shrink-0" />
              <span>Merchant Webhooks</span>
              <span className="ml-auto text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                F-02
              </span>
            </button>

            <button
              onClick={() => onTabChange('risk')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'risk'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <ShieldCheck className="w-4 h-4 shrink-0" />
              <span>Risk Review Queue</span>
              <span className="ml-auto text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                F-03
              </span>
            </button>

            <button
              onClick={() => onTabChange('api-keys')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'api-keys'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <Key className="w-4 h-4 shrink-0" />
              <span>Merchant API Keys</span>
              <span className="ml-auto text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                F-04
              </span>
            </button>

            <button
              onClick={() => onTabChange('webhook-dlq')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'webhook-dlq'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <AlertTriangle className="w-4 h-4 shrink-0 text-amber-400" />
              <span>Webhook DLQ</span>
              <span className="ml-auto text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                F-05
              </span>
            </button>

            <button
              onClick={() => onTabChange('observability')}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition text-left ${
                activeTab === 'observability'
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'text-gray-300 hover:bg-[#1f2937] hover:text-white'
              }`}
            >
              <Activity className="w-4 h-4 shrink-0 text-cyan-400" />
              <span>Observability</span>
              <span className="ml-auto text-[10px] bg-cyan-500/20 text-cyan-300 border border-cyan-500/30 px-1.5 py-0.2 rounded font-mono">
                OPS
              </span>
            </button>
          </nav>

          <div className="mt-auto pt-6 border-t border-[#374151] hidden md:block">
            <div className="flex items-center gap-2 text-xs text-gray-400 px-3">
              <Activity className="w-3.5 h-3.5 text-emerald-400" />
              <span>Gateway: Online (200 OK)</span>
            </div>
          </div>
        </aside>

        {/* Main Content Area */}
        <main className="flex-1 p-4 md:p-8 max-w-7xl mx-auto w-full">
          {children}
        </main>
      </div>
    </div>
  );
};
