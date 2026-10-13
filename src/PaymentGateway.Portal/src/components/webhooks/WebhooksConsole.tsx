import React, { useState, useEffect, useCallback } from 'react';
import { useAuth } from '../../context/AuthContext';
import {
  fetchWebhookSubscription,
  updateWebhookSubscription,
  rotateWebhookSecret,
  dispatchTestWebhook,
  ApiError,
} from '../../services/apiClient';
import {
  MerchantWebhookSubscriptionDto,
  TestWebhookDispatchResultDto,
} from '../../types/api';
import { WebhookConfigCard } from './WebhookConfigCard';
import { SecretKeyCard } from './SecretKeyCard';
import { EventSimulatorPanel } from './EventSimulatorPanel';
import { CardSkeleton } from '../common/LoadingSkeleton';

export const WebhooksConsole: React.FC = () => {
  const { user, token, setRole } = useAuth();
  const role = user.role;
  const [subscription, setSubscription] = useState<MerchantWebhookSubscriptionDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isRotating, setIsRotating] = useState(false);
  const [isDispatching, setIsDispatching] = useState(false);
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);

  const isAuthorized = role === 'merchant' || role === 'ops';

  const partyId = user.merchantId || 'merch_acme';

  const loadSubscription = useCallback(async () => {
    if (!token || !isAuthorized) {
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    setGlobalError(null);

    try {
      const data = await fetchWebhookSubscription(token, partyId);
      setSubscription(data);
    } catch (err: unknown) {
      if (err instanceof ApiError && err.status === 404) {
        // No subscription created yet: initialize empty scaffold for this merchant
        setSubscription({
          id: '',
          partyId,
          endpointUrl: '',
          secretKey: '',
          isActive: false,
        });
      } else if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to load webhook configuration.');
      }
    } finally {
      setIsLoading(false);
    }
  }, [token, isAuthorized, partyId]);

  useEffect(() => {
    loadSubscription();
  }, [loadSubscription]);

  const showNotification = (msg: string) => {
    setSuccessNotice(msg);
    setTimeout(() => setSuccessNotice(null), 4000);
  };

  const handleSave = async (endpointUrl: string, isActive: boolean) => {
    if (!token) return;
    setIsSaving(true);
    setGlobalError(null);
    try {
      const updated = await updateWebhookSubscription(token, { endpointUrl, isActive }, partyId);
      setSubscription(updated);
      showNotification('Webhook configuration saved successfully.');
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to update webhook subscription.');
      }
      throw err;
    } finally {
      setIsSaving(false);
    }
  };

  const handleRotate = async () => {
    if (!token) return;
    setIsRotating(true);
    setGlobalError(null);
    try {
      const updated = await rotateWebhookSecret(token, partyId);
      setSubscription(updated);
      showNotification('Secret key rotated successfully. Remember to update your verification endpoint.');
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to rotate secret key.');
      }
      throw err;
    } finally {
      setIsRotating(false);
    }
  };

  const handleDispatch = async (
    endpointUrl?: string,
    eventType?: string
  ): Promise<TestWebhookDispatchResultDto> => {
    if (!token) {
      throw new Error('Authentication token is required.');
    }
    setIsDispatching(true);
    try {
      const result = await dispatchTestWebhook(token, { endpointUrl, eventType }, partyId);
      return result;
    } finally {
      setIsDispatching(false);
    }
  };

  if (!isAuthorized) {
    return (
      <div className="space-y-6">
        <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-8 text-center max-w-xl mx-auto my-12">
          <div className="w-12 h-12 rounded-full bg-amber-500/20 text-amber-400 flex items-center justify-center mx-auto mb-4">
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-gray-100 mb-2">Merchant Access Required</h2>
          <p className="text-xs text-gray-400 mb-6 leading-relaxed">
            The Webhook Self-Service console is restricted to authenticated merchants (or operators in diagnostic mode). Your current role is <strong className="text-amber-300 font-mono">{role}</strong>.
          </p>
          <button
            type="button"
            onClick={() => setRole('merchant')}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-blue-600 hover:bg-blue-500 text-white transition-colors cursor-pointer"
          >
            Switch to Merchant Role
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b border-gray-800 pb-5">
        <div>
          <h1 className="text-2xl font-bold text-gray-100 tracking-tight">
            Merchant Webhooks &amp; Diagnostics
          </h1>
          <p className="text-xs text-gray-400 mt-1">
            Configure callback notifications, manage HMAC signing credentials, and test connectivity with the live event simulator.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium text-gray-400">Authenticated Party:</span>
          <span className="text-xs font-mono text-blue-400 bg-blue-500/10 px-2.5 py-1 rounded-md border border-blue-500/20 font-semibold">
            {partyId}
          </span>
        </div>
      </div>

      {/* Notifications */}
      {successNotice && (
        <div className="p-4 bg-emerald-500/10 border border-emerald-500/30 rounded-xl text-xs text-emerald-400 flex items-center justify-between animate-in fade-in-0">
          <div className="flex items-center gap-2">
            <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
            </svg>
            <span>{successNotice}</span>
          </div>
          <button
            type="button"
            onClick={() => setSuccessNotice(null)}
            className="text-emerald-400/80 hover:text-emerald-300 cursor-pointer"
          >
            &times;
          </button>
        </div>
      )}

      {globalError && (
        <div className="p-4 bg-rose-500/10 border border-rose-500/30 rounded-xl text-xs text-rose-400 flex items-center justify-between animate-in fade-in-0">
          <span>{globalError}</span>
          <button
            type="button"
            onClick={() => setGlobalError(null)}
            className="text-rose-400/80 hover:text-rose-300 cursor-pointer"
          >
            &times;
          </button>
        </div>
      )}

      {/* Main Split-Panel Content */}
      {isLoading ? (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <div className="space-y-6">
            <CardSkeleton />
            <CardSkeleton />
          </div>
          <CardSkeleton />
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Left Panel: Configuration and Secret Key */}
          <div className="space-y-6">
            <WebhookConfigCard
              partyId={partyId}
              initialEndpointUrl={subscription?.endpointUrl || ''}
              initialIsActive={subscription?.isActive ?? true}
              isLoading={isLoading}
              isSaving={isSaving}
              onSave={handleSave}
            />

            <SecretKeyCard
              secretKey={subscription?.secretKey || ''}
              isLoading={isLoading}
              isRotating={isRotating}
              onRotate={handleRotate}
            />
          </div>

          {/* Right Panel: Interactive Event Simulator */}
          <div>
            <EventSimulatorPanel
              configuredEndpointUrl={subscription?.endpointUrl || ''}
              isDispatching={isDispatching}
              onDispatch={handleDispatch}
            />
          </div>
        </div>
      )}
    </div>
  );
};
