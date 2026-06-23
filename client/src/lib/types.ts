// Mirrors the JSON contracts emitted by the ASP.NET API (see server/Models/OntologyDtos.cs).

export interface OntologyMeta {
  iri: string | null
  title: string | null
  comment: string | null
  sourceName: string
  imports: string[]
}

export interface Restriction {
  onProperty: string | null
  onPropertyName: string | null
  kind: 'some' | 'all' | 'value' | 'min' | 'max' | 'exactly' | string
  filler: string | null
  fillerName: string | null
  cardinality: number | null
  display: string
}

export interface OwlClass {
  id: string
  name: string
  label: string | null
  comment: string | null
  subClassOf: string[]
  disjointWith: string[]
  equivalentClasses: string[]
  restrictions: Restriction[]
  instanceCount: number
  declared: boolean
}

export interface OwlProperty {
  id: string
  name: string
  label: string | null
  comment: string | null
  kind: 'object' | 'datatype' | 'annotation'
  domains: string[]
  ranges: string[]
  inverseOf: string | null
  subPropertyOf: string[]
  characteristics: string[]
}

export interface Assertion {
  property: string
  propertyName: string
  value: string
  valueName: string | null
  datatype: string | null
  isObject: boolean
}

export interface OwlIndividual {
  id: string
  name: string
  label: string | null
  comment: string | null
  types: string[]
  assertions: Assertion[]
}

export interface Stats {
  classes: number
  objectProperties: number
  datatypeProperties: number
  annotationProperties: number
  individuals: number
  restrictions: number
  disjointAxioms: number
  subClassAxioms: number
}

export interface Ontology {
  meta: OntologyMeta
  classes: OwlClass[]
  properties: OwlProperty[]
  individuals: OwlIndividual[]
  stats: Stats
}

export type EntityKind = 'class' | 'property' | 'individual'

export interface Selection {
  kind: EntityKind
  id: string
}

export interface GraphFilters {
  showClasses: boolean
  showIndividuals: boolean
  subClassOf: boolean
  disjoint: boolean
  restriction: boolean
  domainRange: boolean
  typeOf: boolean
  assertion: boolean
}
