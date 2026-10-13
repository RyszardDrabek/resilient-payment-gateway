import { describe, it, expect } from 'vitest';
import { ApiError } from './apiClient';

describe('apiClient ApiError', () => {
  it('correctly captures HTTP status code and RFC 7807 problem details', () => {
    const error = new ApiError(403, 'The caller does not have the required role to access this resource.', {
      type: 'urn:rpg:problem:forbidden',
      title: 'Forbidden',
      status: 403,
      detail: 'The caller does not have the required role to access this resource.',
      correlationId: 'test-corr-123',
    });

    expect(error.status).toBe(403);
    expect(error.message).toBe('The caller does not have the required role to access this resource.');
    expect(error.problem?.correlationId).toBe('test-corr-123');
    expect(error.problem?.type).toBe('urn:rpg:problem:forbidden');
  });
});
