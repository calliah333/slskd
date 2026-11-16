import React from 'react';
import { Loader } from 'semantic-ui-react';

// matches the default height of Shared/Graph, for graph placeholders
export const GRAPH_HEIGHT = 200;

// placeholder for a single dashboard section whose report hasn't arrived yet;
// a fixed height keeps the layout from jumping when the content fills in
const SectionLoader = ({ height }) => (
  <div
    style={{
      alignItems: 'center',
      display: 'flex',
      height,
      justifyContent: 'center',
    }}
  >
    <Loader
      active
      inline
      size="small"
    />
  </div>
);

export default SectionLoader;
