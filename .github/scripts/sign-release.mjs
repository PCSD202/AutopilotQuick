import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import * as openpgp from 'openpgp';

const archivePath = process.argv[2];
const publicKeyPath = process.argv[3] ?? 'AutopilotQuick/Resources/AutopilotQuick_PubKey.asc';
let stage = 'loading release inputs';
try {
  if (!archivePath || !process.env.AQ_SIGNING_KEY) throw new Error();
  const hash = createHash('sha256').update(await readFile(archivePath)).digest('hex').toUpperCase();
  stage = 'importing the signing key';
  const encryptedKey = await openpgp.readPrivateKey({ armoredKey: process.env.AQ_SIGNING_KEY });
  stage = 'unlocking the signing key';
  const privateKey = encryptedKey.isDecrypted() ? encryptedKey : await openpgp.decryptKey({
    privateKey: encryptedKey, passphrase: process.env.AQ_SIGNING_PASSPHRASE ?? ''
  });
  stage = 'signing the release hash';
  const signature = await openpgp.sign({
    message: await openpgp.createCleartextMessage({ text: hash }),
    signingKeys: privateKey,
    config: { preferredHashAlgorithm: openpgp.enums.hash.sha256 }
  });
  stage = 'verifying against the app public key';
  const verification = await openpgp.verify({
    message: await openpgp.readCleartextMessage({ cleartextMessage: signature }),
    verificationKeys: await openpgp.readKey({ armoredKey: await readFile(publicKeyPath, 'utf8') })
  });
  if (verification.data !== hash || verification.signatures.length !== 1) throw new Error();
  await verification.signatures[0].verified;
  await writeFile(archivePath + '.sha256.pgp', signature, 'utf8');
  console.log('Release hash signed and verified against the app public key.');
} catch {
  console.error(`Release signing failed while ${stage}.`);
  process.exitCode = 1;
} finally {
  delete process.env.AQ_SIGNING_KEY;
  delete process.env.AQ_SIGNING_PASSPHRASE;
}
