import { Icon, Item, Loader } from 'semantic-ui-react';

const ImagePlaceholder = () => (
  <div className="users-picture-placeholder ui small image">
    <Icon
      name="camera"
      size="big"
    />
  </div>
);

const Presence = ({ presence }) => {
  const colors = {
    Away: 'yellow',
    Online: 'green',
  };

  return (
    <Icon
      color={colors[presence] || 'grey'}
      name="circle"
    />
  );
};

const FreeUploadSlot = ({ hasFreeUploadSlot }) => (
  <Icon
    color={hasFreeUploadSlot ? 'green' : 'red'}
    name={hasFreeUploadSlot ? 'check' : 'close'}
  />
);

const UserDetails = ({
  address,
  hasFreeUploadSlot,
  infoError,
  infoPending,
  port,
  queueLength,
  uploadSlots,
}) => {
  const endpoint =
    address === undefined ? '' : `, IP Address: ${address}, Port: ${port}`;

  if (infoPending) {
    return (
      <>
        <Loader
          active
          inline
          size="mini"
        />{' '}
        Requesting info from the user{endpoint}
      </>
    );
  }

  if (infoError) {
    return `Info unavailable${endpoint}`;
  }

  return (
    <>
      Free Upload Slot: <FreeUploadSlot hasFreeUploadSlot={hasFreeUploadSlot} />
      , Total Upload Slots: {uploadSlots}, Queue Length: {queueLength}
      {endpoint}
    </>
  );
};

const User = ({
  address,
  description,
  hasFreeUploadSlot,
  hasPicture,
  infoError,
  infoPending,
  picture,
  port,
  presence,
  queueLength,
  uploadSlots,
  username,
}) => (
  <Item>
    {hasPicture ? (
      <Item.Image
        size="small"
        src={`data:image;base64,${picture}`}
      />
    ) : (
      <ImagePlaceholder />
    )}

    <Item.Content>
      <Item.Header as="a">
        <Presence presence={presence} />
        {username}
      </Item.Header>
      <Item.Meta>
        <UserDetails
          address={address}
          hasFreeUploadSlot={hasFreeUploadSlot}
          infoError={infoError}
          infoPending={infoPending}
          port={port}
          queueLength={queueLength}
          uploadSlots={uploadSlots}
        />
      </Item.Meta>
      <Item.Description>
        {infoError
          ? String(infoError)
          : infoPending
            ? ''
            : description || 'No user info.'}
      </Item.Description>
    </Item.Content>
  </Item>
);

export default User;
