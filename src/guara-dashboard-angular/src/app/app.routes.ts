import { Routes } from '@angular/router';

// Lazy por rota: cada tela vira um chunk próprio (first load enxuto — spec 024 AC-6).
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/overview/overview.component').then((m) => m.OverviewComponent),
  },
  {
    path: 'jobs',
    loadComponent: () => import('./features/jobs/jobs.component').then((m) => m.JobsComponent),
  },
  {
    path: 'jobs/:id',
    loadComponent: () => import('./features/job-detail/job-detail.component').then((m) => m.JobDetailComponent),
  },
  {
    path: 'recurring',
    loadComponent: () => import('./features/recurring/recurring.component').then((m) => m.RecurringComponent),
  },
  {
    path: 'servers',
    loadComponent: () => import('./features/servers/servers.component').then((m) => m.ServersComponent),
  },
  { path: '**', redirectTo: '' },
];
