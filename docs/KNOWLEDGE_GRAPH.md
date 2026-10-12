# Knowledge graph

The current graph model uses `KnowledgeNode` and `KnowledgeEdge` records.
Nodes are typed project entities (modules, classes, APIs, services, tests,
requirements, memory, etc.). Edges encode relationships such as
`DependsOn`, `Implements`, `Calls`, `Contains`, `TestedBy`, and `RelatedTo`.

`KnowledgeController` loads owner-visible project nodes/edges and serializes
Cytoscape elements for a Razor view. Each request is bounded to 500 nodes and
1,000 edges; the page reports when the overview omits additional records.
The graph has an accessible text alternative. Edges are scoped to nodes visible
to the caller and to nodes included in the bounded graph, so an edge cannot
disclose another project's identifiers.

The graph is currently a storage and visualization foundation: repository
scanning, automatic extraction from execution output, deduplication,
confidence/provenance, graph mutation workflows, neighborhood expansion,
and graph-query APIs are not implemented. Do not assume a node exists
unless an explicit ingestion path has created it.
