import { HttpInterceptorFn } from '@angular/common/http';
import { Injector } from '@angular/core';

export interface AppEnvironment {
  mock: boolean;
  /** Extra HTTP interceptors, after the API interceptor. */
  interceptors: HttpInterceptorFn[];
  /** Mock only: forget the mock data and reload. */
  resetMock?: (injector: Injector) => void;
}
