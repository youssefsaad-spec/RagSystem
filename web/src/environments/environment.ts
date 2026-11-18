import { AppEnvironment } from './environment.model';

/** Real API: requests go through the dev proxy or the reverse proxy. Nothing of the mock is part of this build. */
export const environment: AppEnvironment = { mock: false, interceptors: [] };
