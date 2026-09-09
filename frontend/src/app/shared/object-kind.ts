/**
 * Colores y etiquetas por tipo de objeto.
 *
 * El mismo color se usa en el grafo, en la traza, en los selectores y en las listas. No es
 * decoración: en un grafo de relaciones, poder distinguir de un vistazo un equipo de una
 * organización es la diferencia entre entenderlo y no entenderlo.
 */
export interface ObjectKindStyle {
  label: string;
  /** Clases de Tailwind para las insignias. */
  chip: string;
  /** Color hexadecimal para Cytoscape, que no entiende de clases CSS. */
  hex: string;
  icon: string;
}

const KINDS: Record<string, ObjectKindStyle> = {
  user: {
    label: 'Usuario',
    chip: 'bg-sky-500/15 text-sky-300 ring-sky-500/30',
    hex: '#38bdf8',
    icon: '👤',
  },
  organization: {
    label: 'Organización',
    chip: 'bg-rose-500/15 text-rose-300 ring-rose-500/30',
    hex: '#fb7185',
    icon: '🏢',
  },
  team: {
    label: 'Equipo',
    chip: 'bg-emerald-500/15 text-emerald-300 ring-emerald-500/30',
    hex: '#34d399',
    icon: '👥',
  },
  group: {
    label: 'Grupo',
    chip: 'bg-amber-500/15 text-amber-300 ring-amber-500/30',
    hex: '#fbbf24',
    icon: '🔖',
  },
  project: {
    label: 'Proyecto',
    chip: 'bg-violet-500/15 text-violet-300 ring-violet-500/30',
    hex: '#a78bfa',
    icon: '📦',
  },
  folder: {
    label: 'Carpeta',
    chip: 'bg-cyan-500/15 text-cyan-300 ring-cyan-500/30',
    hex: '#22d3ee',
    icon: '📁',
  },
  resource: {
    label: 'Recurso',
    chip: 'bg-slate-500/15 text-slate-300 ring-slate-500/30',
    hex: '#94a3b8',
    icon: '📄',
  },
};

const FALLBACK: ObjectKindStyle = {
  label: 'Objeto',
  chip: 'bg-slate-500/15 text-slate-300 ring-slate-500/30',
  hex: '#64748b',
  icon: '•',
};

/** Extrae el tipo de una referencia `tipo:id` o `tipo:id#relación`. */
export function objectKind(reference: string): string {
  const separator = reference.indexOf(':');
  return separator <= 0 ? reference : reference.slice(0, separator);
}

export function kindStyle(reference: string): ObjectKindStyle {
  return KINDS[objectKind(reference)] ?? FALLBACK;
}

/** `true` si la referencia es un userset (`team:backend#member`) y no un individuo. */
export function isUserset(reference: string): boolean {
  return reference.includes('#');
}

export const OBJECT_KINDS = KINDS;
