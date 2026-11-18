import { Injector } from '@angular/core';
import { MOCK_BACKEND, mockBackendInterceptor, resetMockData } from '../app/core/mock/mock-backend.interceptor';
import { AppEnvironment } from './environment.model';

/** Mock API: the browser answers /api itself with data in the shape of the backend DTOs. Used by `npm run start:mock`. */
export const environment: AppEnvironment = {
  mock: true,
  interceptors: [mockBackendInterceptor],
  resetMock: (injector: Injector) => resetMockData(injector.get(MOCK_BACKEND)),
};
